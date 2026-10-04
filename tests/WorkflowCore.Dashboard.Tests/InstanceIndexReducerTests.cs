using WorkflowCore.Dashboard.Journal;

namespace WorkflowCore.Dashboard.Tests;

public class InstanceIndexReducerTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Start_then_complete_gives_a_complete_row_with_both_times()
    {
        var changes = InstanceIndexReducer.Reduce([
            Entries.Event("WorkflowStarted", "wf-1", T0),
            Entries.Event("WorkflowCompleted", "wf-1", T0.AddSeconds(5)),
        ]);

        var row = InstanceIndexReducer.Apply(null, Assert.Single(changes));

        Assert.Equal("Complete", row.Status);
        Assert.Equal(T0, row.CreateTime);
        Assert.Equal(T0.AddSeconds(5), row.CompleteTime);
    }

    [Fact]
    public void A_late_start_event_does_not_overwrite_a_newer_status_but_fixes_the_creation_time()
    {
        // The completion is processed first (other batch or other node), so the row guesses its creation time.
        var row = InstanceIndexReducer.Apply(null, InstanceIndexReducer.Reduce([
            Entries.Event("WorkflowCompleted", "wf-1", T0.AddSeconds(5)),
        ])[0]);
        Assert.Equal(T0.AddSeconds(5), row.CreateTime);

        row = InstanceIndexReducer.Apply(row, InstanceIndexReducer.Reduce([
            Entries.Event("WorkflowStarted", "wf-1", T0),
        ])[0]);

        Assert.Equal("Complete", row.Status);
        Assert.Equal(T0, row.CreateTime);
        Assert.Equal(T0.AddSeconds(5), row.CompleteTime);
    }

    [Fact]
    public void Suspend_and_resume_track_the_latest_status_and_errors_accumulate()
    {
        var row = InstanceIndexReducer.Apply(null, InstanceIndexReducer.Reduce([
            Entries.Event("WorkflowStarted", "wf-1", T0),
            Entries.Event("WorkflowError", "wf-1", T0.AddSeconds(1)),
            Entries.Event("WorkflowSuspended", "wf-1", T0.AddSeconds(1)),
        ])[0]);
        Assert.Equal("Suspended", row.Status);

        row = InstanceIndexReducer.Apply(row, InstanceIndexReducer.Reduce([
            Entries.Event("WorkflowResumed", "wf-1", T0.AddSeconds(10)),
            Entries.Event("WorkflowError", "wf-1", T0.AddSeconds(11)),
        ])[0]);

        Assert.Equal("Runnable", row.Status);
        Assert.Null(row.CompleteTime);
        Assert.Equal(2, row.ErrorCount);
        Assert.Equal(T0.AddSeconds(11), row.LastEventTime);
    }
}
