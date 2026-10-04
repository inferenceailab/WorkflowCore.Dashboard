using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.Journal;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Api;

internal sealed record InstanceListQuery(
    WorkflowStatus? Status,
    string? DefinitionId,
    DateTime? CreatedFrom,
    DateTime? CreatedTo,
    int Skip,
    int Take);

/// <summary>
/// Lists instances either from Workflow Core's persistence provider or from the journal's instance index,
/// according to <see cref="DashboardOptions.InstanceListing"/>.
/// </summary>
internal sealed class InstanceListing
{
    // Index rows whose instance is gone from the store this long after their last event were purged by Workflow Core.
    private static readonly TimeSpan PurgedAfter = TimeSpan.FromMinutes(10);
    private const int MaxParallelLoads = 8;

    private readonly IPersistenceProvider _store;
    private readonly IWorkflowRegistry _registry;
    private readonly IDashboardJournal _journal;
    private readonly DashboardOptions _options;

    public InstanceListing(IPersistenceProvider store, IWorkflowRegistry registry, IDashboardJournal journal, IOptions<DashboardOptions> options)
    {
        _store = store;
        _registry = registry;
        _journal = journal;
        _options = options.Value;
    }

    /// <summary>Returns null when the configured source cannot list instances.</summary>
    public async Task<Page<InstanceSummary>?> List(InstanceListQuery query, CancellationToken ct)
    {
        var mode = _options.InstanceListing;
        if (mode == InstanceListingSource.Journal || (mode == InstanceListingSource.Auto && _journal.IsPersistent))
            return await FromJournal(query, ct);

        var page = await FromProvider(query);
        if (page is not null || mode == InstanceListingSource.Provider)
            return page;

        // Redis, Cosmos DB and DynamoDB cannot list; the index still knows the instances seen since startup.
        return await FromJournal(query, ct);
    }

    private async Task<Page<InstanceSummary>?> FromProvider(InstanceListQuery q)
    {
        List<WorkflowInstance> page;
        try
        {
            // Obsolete in Workflow Core, but it is the only provider-agnostic listing API.
            // One extra row is requested to tell whether a next page exists.
#pragma warning disable CS0612, CS0618
            page = (await _store.GetWorkflowInstances(q.Status, q.DefinitionId, q.CreatedFrom, q.CreatedTo, q.Skip, q.Take + 1)).ToList();
#pragma warning restore CS0612, CS0618
        }
        catch (NotImplementedException)
        {
            return null;
        }

        var items = page.Take(q.Take).Select(Summarize).ToList();
        return new Page<InstanceSummary>(items, q.Skip, q.Take, page.Count > q.Take, null, "provider");
    }

    private async Task<Page<InstanceSummary>> FromJournal(InstanceListQuery q, CancellationToken ct)
    {
        var result = await _journal.QueryInstancesAsync(
            new InstanceIndexQuery(q.Status?.ToString(), q.DefinitionId, q.CreatedFrom, q.CreatedTo, q.Skip, q.Take), ct);

        // The index decides which instances and in what order; the store supplies their current state.
        var loaded = await Load(result.Items.Select(r => r.InstanceId).ToList(), ct);
        var items = new List<InstanceSummary>(result.Items.Count);
        var purged = new List<string>();
        foreach (var row in result.Items)
        {
            if (loaded.TryGetValue(row.InstanceId, out var wf))
            {
                items.Add(Summarize(wf));
                continue;
            }

            items.Add(DtoMapper.ToSummary(row));
            if (row.LastEventTime < DateTime.UtcNow - PurgedAfter)
                purged.Add(row.InstanceId);
        }

        if (purged.Count > 0)
            await _journal.RemoveInstancesAsync(purged, ct);

        return new Page<InstanceSummary>(items, q.Skip, q.Take, q.Skip + items.Count < result.Total, result.Total, "journal");
    }

    private async Task<Dictionary<string, WorkflowInstance>> Load(IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return new Dictionary<string, WorkflowInstance>();

        try
        {
            return (await _store.GetWorkflowInstances(ids, ct)).ToDictionary(w => w.Id);
        }
        catch (NotImplementedException)
        {
            // Cosmos DB cannot load several instances at once; load them one by one.
            var result = new Dictionary<string, WorkflowInstance>();
            using var gate = new SemaphoreSlim(MaxParallelLoads);
            var loads = ids.Select(async id =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await _store.GetWorkflowInstance(id, ct);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
                finally
                {
                    gate.Release();
                }
            });
            foreach (var wf in await Task.WhenAll(loads))
            {
                if (wf is not null)
                    result[wf.Id] = wf;
            }
            return result;
        }
    }

    private InstanceSummary Summarize(WorkflowInstance wf) =>
        DtoMapper.ToSummary(wf, _registry.GetDefinition(wf.WorkflowDefinitionId, wf.Version));
}
