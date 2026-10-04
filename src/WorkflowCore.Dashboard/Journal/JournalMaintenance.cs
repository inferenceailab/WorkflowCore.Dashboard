using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Journal;

/// <summary>
/// Background upkeep for persistent journals: a one-time backfill of the instance index from the
/// persistence provider, then hourly purging of activity older than the retention period.
/// </summary>
internal sealed class JournalMaintenance : BackgroundService
{
    internal const string BackfillKey = "index-backfill";
    private const int BackfillPageSize = 200;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private readonly IDashboardJournal _journal;
    private readonly IServiceProvider _services;
    private readonly DashboardOptions _options;
    private readonly ILogger<JournalMaintenance> _logger;

    public JournalMaintenance(
        IDashboardJournal journal, IServiceProvider services, IOptions<DashboardOptions> options, ILogger<JournalMaintenance> logger)
    {
        _journal = journal;
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_journal.IsPersistent)
            return;

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            if (_options.BackfillIndex)
                await RunGuarded("index backfill", () => Backfill(stoppingToken));

            using var timer = new PeriodicTimer(PurgeInterval);
            do
            {
                if (_options.JournalRetention > TimeSpan.Zero)
                    await RunGuarded("journal purge", () => _journal.PurgeAsync(DateTime.UtcNow - _options.JournalRetention, stoppingToken));
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Adds instances created before the dashboard was installed. Runs once per journal store.
    /// Providers that cannot list instances (Redis, Cosmos DB, DynamoDB) are skipped.
    /// </summary>
    private async Task Backfill(CancellationToken ct)
    {
        if (await _journal.GetMetadataAsync(BackfillKey, ct) is not null)
            return;

        var store = _services.GetRequiredService<IPersistenceProvider>();
        var total = 0;
        for (var skip = 0; ; skip += BackfillPageSize)
        {
            List<WorkflowInstance> page;
            try
            {
#pragma warning disable CS0612, CS0618
                page = (await store.GetWorkflowInstances(null, null, null, null, skip, BackfillPageSize)).ToList();
#pragma warning restore CS0612, CS0618
            }
            catch (NotImplementedException)
            {
                _logger.LogInformation("{Provider} cannot list instances; the dashboard index will only contain new instances",
                    store.GetType().Name);
                await _journal.SetMetadataAsync(BackfillKey, "unsupported", ct);
                return;
            }

            await _journal.AddMissingInstancesAsync(page.Select(ToIndexRow).ToList(), ct);
            total += page.Count;
            if (page.Count < BackfillPageSize)
                break;
        }

        await _journal.SetMetadataAsync(BackfillKey, DateTime.UtcNow.ToString("O"), ct);
        _logger.LogInformation("Dashboard index backfilled with {Count} existing workflow instances", total);
    }

    private static InstanceIndexEntry ToIndexRow(WorkflowInstance wf)
    {
        var created = DateTime.SpecifyKind(wf.CreateTime, DateTimeKind.Utc);
        var completed = wf.CompleteTime is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : (DateTime?)null;
        return new InstanceIndexEntry(
            wf.Id,
            wf.WorkflowDefinitionId,
            wf.Version,
            wf.Reference,
            wf.Status.ToString(),
            // Backfilled status is a snapshot; any lifecycle event after it takes precedence.
            completed ?? created,
            created,
            completed,
            wf.ExecutionPointers.Count(p => p.Status == PointerStatus.Failed),
            completed ?? created);
    }

    private async Task RunGuarded(string name, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dashboard {Task} failed", name);
        }
    }
}
