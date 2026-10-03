using Microsoft.Extensions.Options;

namespace WorkflowCore.Dashboard.Live;

public sealed record ActivityEntry(
    long Sequence,
    string Type,
    DateTime Time,
    string InstanceId,
    string DefinitionId,
    int Version,
    string? Reference,
    string? ExecutionPointerId,
    int? StepId,
    string? StepName,
    string? Message,
    string? Details);

/// <summary>
/// Bounded in-memory log of recent lifecycle events. It only covers events seen since the app started.
/// </summary>
public sealed class ActivityFeed
{
    private readonly int _capacity;
    private readonly LinkedList<ActivityEntry> _entries = new();
    private readonly Dictionary<string, long> _totals = new();
    private long _sequence;

    public ActivityFeed(IOptions<DashboardOptions> options)
    {
        _capacity = Math.Max(1, options.Value.ActivityCapacity);
    }

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public ActivityEntry Add(Func<long, ActivityEntry> create)
    {
        lock (_entries)
        {
            var entry = create(++_sequence);
            _entries.AddFirst(entry);
            _totals[entry.Type] = _totals.GetValueOrDefault(entry.Type) + 1;
            while (_entries.Count > _capacity)
                _entries.RemoveLast();
            return entry;
        }
    }

    /// <summary>
    /// Adds exception details to the newest matching WorkflowError entry that has none yet.
    /// Returns the updated entry, or null when no such entry exists.
    /// </summary>
    public ActivityEntry? TryAttachDetails(string instanceId, int stepId, string details)
    {
        lock (_entries)
        {
            for (var node = _entries.First; node is not null; node = node.Next)
            {
                var e = node.Value;
                if (e.Type == "WorkflowError" && e.InstanceId == instanceId && e.StepId == stepId && e.Details is null)
                {
                    node.Value = e with { Details = details };
                    return node.Value;
                }
            }
            return null;
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ActivityEntry> Get(string? instanceId, int take)
    {
        lock (_entries)
        {
            IEnumerable<ActivityEntry> query = _entries;
            if (!string.IsNullOrEmpty(instanceId))
                query = query.Where(x => x.InstanceId == instanceId);
            return query.Take(take).ToList();
        }
    }

    /// <summary>Event counts by type since the app started, not limited by capacity.</summary>
    public IReadOnlyDictionary<string, long> Totals()
    {
        lock (_entries)
        {
            return new Dictionary<string, long>(_totals);
        }
    }
}
