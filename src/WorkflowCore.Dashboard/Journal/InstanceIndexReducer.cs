namespace WorkflowCore.Dashboard.Journal;

/// <summary>The effect of a batch of new activity entries on one instance's index row.</summary>
public sealed record InstanceIndexChange(
    string InstanceId,
    string DefinitionId,
    int Version,
    string? Reference,
    DateTime FirstEventTime,
    DateTime LastEventTime,
    DateTime? CreateTime,
    string? Status,
    DateTime? StatusTime,
    DateTime? CompleteTime,
    int NewErrors);

/// <summary>
/// Turns lifecycle events into instance index updates. Shared by every journal implementation so
/// they agree on how events map to statuses, including when events arrive out of order.
/// </summary>
public static class InstanceIndexReducer
{
    public static IReadOnlyList<InstanceIndexChange> Reduce(IEnumerable<ActivityEntry> newEntries)
    {
        var changes = new List<InstanceIndexChange>();
        foreach (var group in newEntries.GroupBy(e => e.InstanceId))
        {
            var ordered = group.OrderBy(e => e.Time).ToList();
            var first = ordered[0];
            DateTime? createTime = null, statusTime = null, completeTime = null;
            string? status = null;
            var errors = 0;

            foreach (var e in ordered)
            {
                switch (e.Type)
                {
                    case "WorkflowStarted":
                        createTime = e.Time;
                        (status, statusTime) = ("Runnable", e.Time);
                        break;
                    case "WorkflowResumed":
                        (status, statusTime) = ("Runnable", e.Time);
                        break;
                    case "WorkflowSuspended":
                        (status, statusTime) = ("Suspended", e.Time);
                        break;
                    case "WorkflowCompleted":
                        (status, statusTime, completeTime) = ("Complete", e.Time, e.Time);
                        break;
                    case "WorkflowTerminated":
                        (status, statusTime, completeTime) = ("Terminated", e.Time, e.Time);
                        break;
                    case "WorkflowError":
                        errors++;
                        break;
                }
            }

            changes.Add(new InstanceIndexChange(
                group.Key, first.DefinitionId, first.Version, ordered.Select(e => e.Reference).FirstOrDefault(r => r is not null),
                first.Time, ordered[^1].Time, createTime, status, statusTime, completeTime, errors));
        }
        return changes;
    }

    /// <summary>Applies a change to an existing row, or creates the row when there is none.</summary>
    public static InstanceIndexEntry Apply(InstanceIndexEntry? row, InstanceIndexChange change)
    {
        if (row is null)
        {
            return new InstanceIndexEntry(
                change.InstanceId,
                change.DefinitionId,
                change.Version,
                change.Reference,
                change.Status ?? "Runnable",
                change.StatusTime ?? change.FirstEventTime,
                change.CreateTime ?? change.FirstEventTime,
                change.CompleteTime,
                change.NewErrors,
                change.LastEventTime);
        }

        var updated = row with
        {
            Reference = row.Reference ?? change.Reference,
            ErrorCount = row.ErrorCount + change.NewErrors,
            LastEventTime = change.LastEventTime > row.LastEventTime ? change.LastEventTime : row.LastEventTime,
            // A row created from a later event guesses its creation time; the real start event corrects it.
            CreateTime = change.CreateTime is { } created && created < row.CreateTime ? created : row.CreateTime,
        };

        // Events can be processed out of order (batches, several nodes); the latest status wins.
        if (change.Status is not null && change.StatusTime >= row.StatusTime)
        {
            updated = updated with
            {
                Status = change.Status,
                StatusTime = change.StatusTime!.Value,
                CompleteTime = change.CompleteTime ?? (IsFinal(change.Status) ? row.CompleteTime : null),
            };
        }

        return updated;
    }

    private static bool IsFinal(string status) => status is "Complete" or "Terminated";
}
