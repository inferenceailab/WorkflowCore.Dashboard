using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.EntityFramework;
using WorkflowCore.Dashboard.Journal;

namespace WorkflowCore.Dashboard.Tests;

internal static class Entries
{
    private static int _counter;

    public static ActivityEntry Event(string type, string instanceId, DateTime time, string? details = null, string? id = null) =>
        new(id ?? $"e{Interlocked.Increment(ref _counter)}", type, time, instanceId, "Orders", 1, null, null,
            type.StartsWith("Step") || type == "WorkflowError" ? 2 : null, null, type == "WorkflowError" ? "boom" : null, details);
}

/// <summary>Behaviour every journal must share. Run once per implementation.</summary>
public abstract class JournalContractTests
{
    protected static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    protected abstract IDashboardJournal Journal { get; }

    [Fact]
    public async Task Appending_the_same_id_twice_stores_it_once_and_fills_in_details()
    {
        await Journal.AppendAsync([Entries.Event("WorkflowError", "wf-1", T0, id: "err-1")]);
        await Journal.AppendAsync([Entries.Event("WorkflowError", "wf-1", T0, details: "stack trace", id: "err-1")]);

        var activity = await Journal.GetActivityAsync(new ActivityQuery("wf-1", null, 10));
        var entry = Assert.Single(activity);
        Assert.Equal("stack trace", entry.Details);

        var row = Assert.Single((await Journal.QueryInstancesAsync(new InstanceIndexQuery(null, null, null, null, 0, 10))).Items);
        Assert.Equal(1, row.ErrorCount);
    }

    [Fact]
    public async Task Activity_is_newest_first_and_can_hide_step_events()
    {
        await Journal.AppendAsync([
            Entries.Event("WorkflowStarted", "wf-1", T0),
            Entries.Event("StepStarted", "wf-1", T0.AddSeconds(1)),
            Entries.Event("StepCompleted", "wf-1", T0.AddSeconds(2)),
            Entries.Event("WorkflowCompleted", "wf-1", T0.AddSeconds(3)),
        ]);

        var all = await Journal.GetActivityAsync(new ActivityQuery("wf-1", null, 10));
        Assert.Equal(["WorkflowCompleted", "StepCompleted", "StepStarted", "WorkflowStarted"], all.Select(e => e.Type));

        var workflowOnly = await Journal.GetActivityAsync(new ActivityQuery("wf-1", null, 10, IncludeSteps: false));
        Assert.Equal(["WorkflowCompleted", "WorkflowStarted"], workflowOnly.Select(e => e.Type));

        var older = await Journal.GetActivityAsync(new ActivityQuery("wf-1", T0.AddSeconds(2), 10));
        Assert.Equal(["StepStarted", "WorkflowStarted"], older.Select(e => e.Type));
    }

    [Fact]
    public async Task Instances_are_listed_newest_first_with_a_total_and_filters()
    {
        await Journal.AppendAsync([
            Entries.Event("WorkflowStarted", "wf-1", T0),
            Entries.Event("WorkflowStarted", "wf-2", T0.AddMinutes(1)),
            Entries.Event("WorkflowStarted", "wf-3", T0.AddMinutes(2)),
            Entries.Event("WorkflowCompleted", "wf-2", T0.AddMinutes(3)),
        ]);

        var page = await Journal.QueryInstancesAsync(new InstanceIndexQuery(null, null, null, null, 0, 2));
        Assert.Equal(3, page.Total);
        Assert.Equal(["wf-3", "wf-2"], page.Items.Select(r => r.InstanceId));

        var running = await Journal.QueryInstancesAsync(new InstanceIndexQuery("Runnable", null, null, null, 0, 10));
        Assert.Equal(["wf-3", "wf-1"], running.Items.Select(r => r.InstanceId));

        var recent = await Journal.QueryInstancesAsync(new InstanceIndexQuery(null, "Orders", T0.AddSeconds(30), null, 0, 10));
        Assert.Equal(2, recent.Total);
    }

    [Fact]
    public async Task Backfilled_rows_never_overwrite_rows_built_from_events()
    {
        await Journal.AppendAsync([Entries.Event("WorkflowSuspended", "wf-1", T0)]);

        await Journal.AddMissingInstancesAsync([
            new InstanceIndexEntry("wf-1", "Orders", 1, null, "Complete", T0, T0, T0, 0, T0),
            new InstanceIndexEntry("wf-old", "Orders", 1, "ref", "Complete", T0, T0.AddDays(-1), T0, 0, T0),
        ]);

        var rows = (await Journal.QueryInstancesAsync(new InstanceIndexQuery(null, null, null, null, 0, 10))).Items;
        Assert.Equal("Suspended", rows.Single(r => r.InstanceId == "wf-1").Status);
        Assert.Equal("ref", rows.Single(r => r.InstanceId == "wf-old").Reference);

        await Journal.RemoveInstancesAsync(["wf-old"]);
        Assert.Equal(1, (await Journal.QueryInstancesAsync(new InstanceIndexQuery(null, null, null, null, 0, 10))).Total);
    }

    [Fact]
    public async Task Metadata_round_trips()
    {
        Assert.Null(await Journal.GetMetadataAsync("k"));
        await Journal.SetMetadataAsync("k", "1");
        await Journal.SetMetadataAsync("k", "2");
        Assert.Equal("2", await Journal.GetMetadataAsync("k"));
    }
}

public class InMemoryJournalTests : JournalContractTests
{
    protected override IDashboardJournal Journal { get; } = new InMemoryJournal(Options.Create(new DashboardOptions()));
}

public sealed class EntityFrameworkJournalTests : JournalContractTests, IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"wfc-dashboard-{Guid.NewGuid():N}.db");

    public EntityFrameworkJournalTests()
    {
        // An unrelated table first: the journal must add its tables to a database that already has some.
        using (var connection = new SqliteConnection($"Data Source={_file}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Workflow (Id TEXT PRIMARY KEY)";
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<JournalDbContext>()
            .UseSqlite($"Data Source={_file};Pooling=False")
            .ReplaceService<IModelCacheKeyFactory, JournalModelCacheKeyFactory>()
            .Options;
        Journal = new EntityFrameworkJournal(options, null);
    }

    protected override IDashboardJournal Journal { get; }

    [Fact]
    public async Task Purge_deletes_old_activity_but_keeps_the_instance_index()
    {
        await Journal.AppendAsync([
            Entries.Event("WorkflowStarted", "wf-1", T0),
            Entries.Event("WorkflowCompleted", "wf-1", T0.AddDays(10)),
        ]);

        await Journal.PurgeAsync(T0.AddDays(5));

        Assert.Equal(["WorkflowCompleted"], (await Journal.GetActivityAsync(new ActivityQuery(null, null, 10))).Select(e => e.Type));
        Assert.Equal("Complete", Assert.Single((await Journal.QueryInstancesAsync(new InstanceIndexQuery(null, null, null, null, 0, 10))).Items).Status);
    }

    [Fact]
    public async Task Totals_count_events_in_the_window()
    {
        await Journal.AppendAsync([
            Entries.Event("WorkflowStarted", "wf-1", T0),
            Entries.Event("WorkflowStarted", "wf-2", T0.AddHours(2)),
            Entries.Event("WorkflowError", "wf-2", T0.AddHours(2)),
        ]);

        var totals = await Journal.GetTotalsAsync(T0.AddHours(1));
        Assert.Equal(1, totals.Counts["WorkflowStarted"]);
        Assert.Equal(1, totals.Counts["WorkflowError"]);
    }

    [Fact]
    public void Times_come_back_as_utc()
    {
        Journal.AppendAsync([Entries.Event("WorkflowStarted", "wf-1", T0)]).GetAwaiter().GetResult();
        var entry = Journal.GetActivityAsync(new ActivityQuery(null, null, 1)).GetAwaiter().GetResult().Single();
        Assert.Equal(DateTimeKind.Utc, entry.Time.Kind);
        Assert.Equal(T0, entry.Time);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_file);
    }
}
