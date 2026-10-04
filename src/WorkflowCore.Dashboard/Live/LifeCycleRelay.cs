using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.Api;
using WorkflowCore.Dashboard.Journal;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using WorkflowCore.Models.LifeCycleEvents;

namespace WorkflowCore.Dashboard.Live;

/// <summary>
/// Turns Workflow Core lifecycle events into activity entries, pushes them to dashboard clients
/// and queues them for the journal.
/// </summary>
internal sealed class LifeCycleRelay : IHostedService
{
    private const int MaxUnmatched = 1000;

    private readonly IWorkflowHost _host;
    private readonly IWorkflowRegistry _registry;
    private readonly JournalWriter _writer;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly DashboardOptions _options;
    private readonly ILogger<LifeCycleRelay> _logger;

    // OnStepError carries the exception; the WorkflowError lifecycle event only has its message.
    // The two arrive on different threads in either order, so whichever comes second joins them.
    private readonly object _errorLock = new();
    private readonly Dictionary<string, string> _detailsWithoutEvent = new();
    private readonly Dictionary<string, ActivityEntry> _eventsWithoutDetails = new();

    public LifeCycleRelay(
        IWorkflowHost host,
        IWorkflowRegistry registry,
        JournalWriter writer,
        IHubContext<DashboardHub> hub,
        IOptions<DashboardOptions> options,
        ILogger<LifeCycleRelay> logger)
    {
        _host = host;
        _registry = registry;
        _writer = writer;
        _hub = hub;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _host.OnLifeCycleEvent += OnLifeCycleEvent;
        _host.OnStepError += OnStepError;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _host.OnLifeCycleEvent -= OnLifeCycleEvent;
        _host.OnStepError -= OnStepError;
        return Task.CompletedTask;
    }

    private void OnStepError(WorkflowInstance workflow, WorkflowStep step, Exception exception)
    {
        var key = ErrorKey(workflow.Id, step.Id);
        ActivityEntry? completed = null;
        lock (_errorLock)
        {
            if (_eventsWithoutDetails.Remove(key, out var entry))
                completed = entry with { Details = exception.ToString() };
            else
                Remember(_detailsWithoutEvent, key, exception.ToString());
        }

        // Re-sent with the same ID: clients replace the entry, the journal fills in the details.
        if (completed is not null)
            Publish(completed);
    }

    private void OnLifeCycleEvent(LifeCycleEvent evt)
    {
        try
        {
            var entry = ToEntry(evt);
            if (entry.Type == nameof(WorkflowError) && entry.StepId is { } stepId)
            {
                var key = ErrorKey(entry.InstanceId, stepId);
                lock (_errorLock)
                {
                    if (_detailsWithoutEvent.Remove(key, out var details))
                        entry = entry with { Details = details };
                    else
                        Remember(_eventsWithoutDetails, key, entry);
                }
            }
            Publish(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to relay lifecycle event {EventType}", evt.GetType().Name);
        }
    }

    private void Publish(ActivityEntry entry)
    {
        _ = _hub.Clients.All.SendAsync("activity", entry);
        if (_options.JournalStepEvents || !entry.Type.StartsWith("Step", StringComparison.Ordinal))
            _writer.Enqueue(entry);
    }

    private ActivityEntry ToEntry(LifeCycleEvent evt)
    {
        string? pointerId = null;
        int? stepId = null;
        string? message = null;

        switch (evt)
        {
            case StepStarted started:
                (pointerId, stepId) = (started.ExecutionPointerId, started.StepId);
                break;
            case StepCompleted completed:
                (pointerId, stepId) = (completed.ExecutionPointerId, completed.StepId);
                break;
            case WorkflowError error:
                (pointerId, stepId, message) = (error.ExecutionPointerId, error.StepId, error.Message);
                break;
        }

        var type = evt.GetType().Name;
        var time = DateTime.SpecifyKind(evt.EventTimeUtc, DateTimeKind.Utc);
        var stepName = stepId is { } id ? StepNames.For(_registry, evt.WorkflowDefinitionId, evt.Version, id) : null;

        return new ActivityEntry(
            EntryId(type, evt.WorkflowInstanceId, pointerId, stepId, time),
            type,
            time,
            evt.WorkflowInstanceId,
            evt.WorkflowDefinitionId,
            evt.Version,
            evt.Reference,
            pointerId,
            stepId,
            stepName,
            message,
            null);
    }

    /// <summary>
    /// Same event, same ID: with a distributed lifecycle event hub every node receives every event,
    /// and the journal stores each one once.
    /// </summary>
    internal static string EntryId(string type, string instanceId, string? pointerId, int? stepId, DateTime time)
    {
        var raw = $"{type}|{instanceId}|{pointerId}|{stepId}|{time.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)), 0, 16).ToLowerInvariant();
    }

    private static void Remember<T>(Dictionary<string, T> map, string key, T value)
    {
        if (map.Count >= MaxUnmatched)
            map.Clear();
        map[key] = value;
    }

    private static string ErrorKey(string instanceId, int stepId) => $"{instanceId}:{stepId}";
}
