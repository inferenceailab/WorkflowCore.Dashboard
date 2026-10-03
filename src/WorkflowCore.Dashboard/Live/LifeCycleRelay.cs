using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkflowCore.Dashboard.Api;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using WorkflowCore.Models.LifeCycleEvents;

namespace WorkflowCore.Dashboard.Live;

/// <summary>
/// Records Workflow Core lifecycle events in the <see cref="ActivityFeed"/> and pushes them to dashboard clients.
/// </summary>
internal sealed class LifeCycleRelay : IHostedService
{
    private const int MaxPendingErrors = 1000;

    private readonly IWorkflowHost _host;
    private readonly IWorkflowRegistry _registry;
    private readonly ActivityFeed _feed;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<LifeCycleRelay> _logger;

    // OnStepError carries the exception; the WorkflowError lifecycle event only has its message.
    // The two arrive on different threads in either order, so whichever comes second joins them.
    private readonly object _errorLock = new();
    private readonly Dictionary<string, string> _pendingErrors = new();

    public LifeCycleRelay(
        IWorkflowHost host,
        IWorkflowRegistry registry,
        ActivityFeed feed,
        IHubContext<DashboardHub> hub,
        ILogger<LifeCycleRelay> logger)
    {
        _host = host;
        _registry = registry;
        _feed = feed;
        _hub = hub;
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
        ActivityEntry? updated;
        lock (_errorLock)
        {
            updated = _feed.TryAttachDetails(workflow.Id, step.Id, exception.ToString());
            if (updated is null)
            {
                if (_pendingErrors.Count >= MaxPendingErrors)
                    _pendingErrors.Clear();
                _pendingErrors[ErrorKey(workflow.Id, step.Id)] = exception.ToString();
            }
        }

        // Clients replace entries they already have by sequence number.
        if (updated is not null)
            _ = _hub.Clients.All.SendAsync("activity", updated);
    }

    private void OnLifeCycleEvent(LifeCycleEvent evt)
    {
        try
        {
            ActivityEntry entry;
            lock (_errorLock)
            {
                entry = _feed.Add(sequence => ToEntry(sequence, evt));
            }
            _ = _hub.Clients.All.SendAsync("activity", entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to relay lifecycle event {EventType}", evt.GetType().Name);
        }
    }

    private ActivityEntry ToEntry(long sequence, LifeCycleEvent evt)
    {
        string? pointerId = null;
        int? stepId = null;
        string? message = null;
        string? details = null;

        switch (evt)
        {
            case StepStarted started:
                pointerId = started.ExecutionPointerId;
                stepId = started.StepId;
                break;
            case StepCompleted completed:
                pointerId = completed.ExecutionPointerId;
                stepId = completed.StepId;
                break;
            case WorkflowError error:
                pointerId = error.ExecutionPointerId;
                stepId = error.StepId;
                message = error.Message;
                var key = ErrorKey(error.WorkflowInstanceId, error.StepId);
                if (_pendingErrors.Remove(key, out var pending))
                    details = pending;
                break;
        }

        var stepName = stepId is { } id
            ? StepNames.For(_registry, evt.WorkflowDefinitionId, evt.Version, id)
            : null;

        return new ActivityEntry(
            sequence,
            evt.GetType().Name,
            DateTime.SpecifyKind(evt.EventTimeUtc, DateTimeKind.Utc),
            evt.WorkflowInstanceId,
            evt.WorkflowDefinitionId,
            evt.Version,
            evt.Reference,
            pointerId,
            stepId,
            stepName,
            message,
            details);
    }

    private static string ErrorKey(string instanceId, int stepId) => $"{instanceId}:{stepId}";
}
