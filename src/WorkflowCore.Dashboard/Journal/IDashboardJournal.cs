namespace WorkflowCore.Dashboard.Journal;

/// <summary>One Workflow Core lifecycle event as recorded by the dashboard.</summary>
/// <param name="Id">Deterministic key derived from the event, so the same event seen by several nodes is stored once.</param>
public sealed record ActivityEntry(
    string Id,
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

public sealed record ActivityQuery(string? InstanceId, DateTime? Before, int Take, bool IncludeSteps = true);

/// <param name="Since">Start of the counted window. In-memory journals count since the app started.</param>
public sealed record ActivityTotals(DateTime Since, IReadOnlyDictionary<string, long> Counts);

/// <summary>What the dashboard knows about an instance from its lifecycle events, used for listing.</summary>
public sealed record InstanceIndexEntry(
    string InstanceId,
    string DefinitionId,
    int Version,
    string? Reference,
    string Status,
    DateTime StatusTime,
    DateTime CreateTime,
    DateTime? CompleteTime,
    int ErrorCount,
    DateTime LastEventTime);

public sealed record InstanceIndexQuery(
    string? Status,
    string? DefinitionId,
    DateTime? CreatedFrom,
    DateTime? CreatedTo,
    int Skip,
    int Take);

public sealed record InstanceIndexPage(IReadOnlyList<InstanceIndexEntry> Items, long Total);

/// <summary>
/// Storage for the activity journal and the instance index. Implementations must treat
/// <see cref="AppendAsync"/> as an upsert keyed by <see cref="ActivityEntry.Id"/>.
/// </summary>
public interface IDashboardJournal
{
    /// <summary>Shown in the UI, e.g. "In-memory" or "Entity Framework (Sqlite)".</summary>
    string Name { get; }

    /// <summary>True when entries survive an app restart.</summary>
    bool IsPersistent { get; }

    /// <summary>
    /// Stores new entries and updates the instance index from them. An entry whose ID already exists
    /// is not stored again; only its missing details are filled in.
    /// </summary>
    Task AppendAsync(IReadOnlyList<ActivityEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ActivityEntry>> GetActivityAsync(ActivityQuery query, CancellationToken cancellationToken = default);

    Task<ActivityTotals> GetTotalsAsync(DateTime since, CancellationToken cancellationToken = default);

    /// <summary>Newest first by creation time, with the total number of matches.</summary>
    Task<InstanceIndexPage> QueryInstancesAsync(InstanceIndexQuery query, CancellationToken cancellationToken = default);

    /// <summary>Adds index rows for instances the index does not know yet. Existing rows are left alone.</summary>
    Task AddMissingInstancesAsync(IReadOnlyList<InstanceIndexEntry> rows, CancellationToken cancellationToken = default);

    Task RemoveInstancesAsync(IReadOnlyCollection<string> instanceIds, CancellationToken cancellationToken = default);

    /// <summary>Deletes activity entries older than <paramref name="cutoff"/>. The instance index is kept.</summary>
    Task PurgeAsync(DateTime cutoff, CancellationToken cancellationToken = default);

    Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default);

    Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>All metadata entries whose key starts with <paramref name="prefix"/>. Used by add-ons to store documents.</summary>
    Task<IReadOnlyDictionary<string, string>> ListMetadataAsync(string prefix, CancellationToken cancellationToken = default);

    Task DeleteMetadataAsync(string key, CancellationToken cancellationToken = default);
}
