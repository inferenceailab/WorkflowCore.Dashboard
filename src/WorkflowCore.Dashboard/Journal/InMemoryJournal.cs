using Microsoft.Extensions.Options;

namespace WorkflowCore.Dashboard.Journal;

/// <summary>
/// Default journal. Keeps the newest <see cref="DashboardOptions.ActivityCapacity"/> entries and an index of up to
/// <see cref="DashboardOptions.InMemoryIndexCapacity"/> instances. Everything is lost when the app stops.
/// </summary>
public sealed class InMemoryJournal : IDashboardJournal
{
    private readonly object _lock = new();
    private readonly int _capacity;
    private readonly int _indexCapacity;
    private readonly LinkedList<ActivityEntry> _entries = new(); // newest first
    private readonly Dictionary<string, LinkedListNode<ActivityEntry>> _byId = new();
    private readonly Dictionary<string, long> _totals = new();
    private readonly Dictionary<string, InstanceIndexEntry> _index = new();
    private readonly Dictionary<string, string> _metadata = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public InMemoryJournal(IOptions<DashboardOptions> options)
    {
        _capacity = Math.Max(1, options.Value.ActivityCapacity);
        _indexCapacity = Math.Max(1, options.Value.InMemoryIndexCapacity);
    }

    public string Name => "In-memory";

    public bool IsPersistent => false;

    public Task AppendAsync(IReadOnlyList<ActivityEntry> entries, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var added = new List<ActivityEntry>();
            foreach (var entry in entries)
            {
                if (_byId.TryGetValue(entry.Id, out var existing))
                {
                    if (existing.Value.Details is null && entry.Details is not null)
                        existing.Value = existing.Value with { Details = entry.Details };
                    continue;
                }

                _byId[entry.Id] = Insert(entry);
                _totals[entry.Type] = _totals.GetValueOrDefault(entry.Type) + 1;
                added.Add(entry);
            }

            while (_entries.Count > _capacity)
            {
                _byId.Remove(_entries.Last!.Value.Id);
                _entries.RemoveLast();
            }

            foreach (var change in InstanceIndexReducer.Reduce(added))
                _index[change.InstanceId] = InstanceIndexReducer.Apply(_index.GetValueOrDefault(change.InstanceId), change);
            TrimIndex();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityEntry>> GetActivityAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IEnumerable<ActivityEntry> result = _entries;
            if (!string.IsNullOrEmpty(query.InstanceId))
                result = result.Where(e => e.InstanceId == query.InstanceId);
            if (query.Before is { } before)
                result = result.Where(e => e.Time < before);
            if (!query.IncludeSteps)
                result = result.Where(e => !e.Type.StartsWith("Step", StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyList<ActivityEntry>>(result.Take(query.Take).ToList());
        }
    }

    /// <summary>Exact counts since the app started; the requested window is ignored.</summary>
    public Task<ActivityTotals> GetTotalsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(new ActivityTotals(_startedAt, new Dictionary<string, long>(_totals)));
        }
    }

    public Task<InstanceIndexPage> QueryInstancesAsync(InstanceIndexQuery query, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var matches = _index.Values.Where(r =>
                    (query.Status is null || r.Status == query.Status) &&
                    (query.DefinitionId is null || r.DefinitionId == query.DefinitionId) &&
                    (query.CreatedFrom is null || r.CreateTime >= query.CreatedFrom) &&
                    (query.CreatedTo is null || r.CreateTime <= query.CreatedTo))
                .OrderByDescending(r => r.CreateTime)
                .ToList();
            return Task.FromResult(new InstanceIndexPage(matches.Skip(query.Skip).Take(query.Take).ToList(), matches.Count));
        }
    }

    public Task AddMissingInstancesAsync(IReadOnlyList<InstanceIndexEntry> rows, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var row in rows)
                _index.TryAdd(row.InstanceId, row);
            TrimIndex();
        }
        return Task.CompletedTask;
    }

    public Task RemoveInstancesAsync(IReadOnlyCollection<string> instanceIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var id in instanceIds)
                _index.Remove(id);
        }
        return Task.CompletedTask;
    }

    public Task PurgeAsync(DateTime cutoff, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_metadata.GetValueOrDefault(key));
        }
    }

    public Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _metadata[key] = value;
        }
        return Task.CompletedTask;
    }

    // Entries usually arrive in time order, so walking from the newest end is short.
    private LinkedListNode<ActivityEntry> Insert(ActivityEntry entry)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            if (entry.Time >= node.Value.Time)
                return _entries.AddBefore(node, entry);
        }
        return _entries.AddLast(entry);
    }

    private void TrimIndex()
    {
        if (_index.Count <= _indexCapacity)
            return;
        foreach (var oldest in _index.Values.OrderBy(r => r.LastEventTime).Take(_index.Count - _indexCapacity).ToList())
            _index.Remove(oldest.InstanceId);
    }
}
