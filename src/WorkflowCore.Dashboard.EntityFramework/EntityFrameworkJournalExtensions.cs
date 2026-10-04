using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace WorkflowCore.Dashboard.EntityFramework;

public static class EntityFrameworkJournalExtensions
{
    /// <summary>
    /// Stores the dashboard's activity journal and instance index in a relational database through EF Core.
    /// Works with any EF Core provider, for example:
    /// <code>
    /// builder.Services.AddWorkflowCoreDashboard()
    ///     .UseEntityFrameworkJournal(db => db.UseSqlServer(connectionString));
    /// </code>
    /// The database can be the one Workflow Core persists to; the dashboard's tables are prefixed <c>WfcDashboard_</c>.
    /// </summary>
    /// <param name="schema">Optional database schema for the dashboard's tables.</param>
    public static DashboardBuilder UseEntityFrameworkJournal(
        this DashboardBuilder builder, Action<DbContextOptionsBuilder> configure, string? schema = null)
    {
        var options = new DbContextOptionsBuilder<JournalDbContext>();
        configure(options);
        options.ReplaceService<IModelCacheKeyFactory, JournalModelCacheKeyFactory>();

        var built = options.Options;
        return builder.UseJournal(_ => new EntityFrameworkJournal(built, schema));
    }
}
