using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorkflowCore.Dashboard.Journal;

namespace WorkflowCore.Dashboard.EntityFramework;

internal sealed class JournalDbContext : DbContext
{
    public JournalDbContext(DbContextOptions<JournalDbContext> options, string? schema)
        : base(options)
    {
        Schema = schema;
    }

    public string? Schema { get; }

    public DbSet<ActivityRow> Activity => Set<ActivityRow>();
    public DbSet<InstanceRow> Instances => Set<InstanceRow>();
    public DbSet<MetadataRow> Metadata => Set<MetadataRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        if (Schema is not null)
            model.HasDefaultSchema(Schema);

        model.Entity<ActivityRow>(e =>
        {
            e.ToTable("WfcDashboard_Activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Type).HasMaxLength(40);
            e.Property(x => x.InstanceId).HasMaxLength(100);
            e.Property(x => x.DefinitionId).HasMaxLength(200);
            e.Property(x => x.Reference).HasMaxLength(200);
            e.Property(x => x.ExecutionPointerId).HasMaxLength(100);
            e.Property(x => x.StepName).HasMaxLength(200);
            e.Property(x => x.Message).HasMaxLength(4000);
            e.HasIndex(x => new { x.InstanceId, x.Time });
            e.HasIndex(x => new { x.Time, x.Type });
        });

        model.Entity<InstanceRow>(e =>
        {
            e.ToTable("WfcDashboard_Instances");
            e.HasKey(x => x.InstanceId);
            e.Property(x => x.InstanceId).HasMaxLength(100);
            e.Property(x => x.DefinitionId).HasMaxLength(200);
            e.Property(x => x.Reference).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.CreateTime);
            e.HasIndex(x => new { x.Status, x.CreateTime });
            e.HasIndex(x => new { x.DefinitionId, x.CreateTime });
        });

        model.Entity<MetadataRow>(e =>
        {
            e.ToTable("WfcDashboard_Metadata");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });

        // Every time is stored as UTC. Providers that drop the kind (SQLite, SQL Server) get it back on read.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        foreach (var property in model.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
                property.SetValueConverter(utc);
            else if (property.ClrType == typeof(DateTime?))
                property.SetValueConverter(utcNullable);
        }
    }
}

/// <summary>The schema is part of the model, so contexts with different schemas need different cached models.</summary>
internal sealed class JournalModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is JournalDbContext journal
            ? (context.GetType(), journal.Schema, designTime)
            : (object)(context.GetType(), designTime);
}

internal sealed class ActivityRow
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? Reference { get; set; }
    public string? ExecutionPointerId { get; set; }
    public int? StepId { get; set; }
    public string? StepName { get; set; }
    public string? Message { get; set; }
    public string? Details { get; set; }

    public static ActivityRow From(ActivityEntry e) => new()
    {
        Id = e.Id,
        Type = e.Type,
        Time = e.Time,
        InstanceId = e.InstanceId,
        DefinitionId = Clip(e.DefinitionId, 200)!,
        Version = e.Version,
        Reference = Clip(e.Reference, 200),
        ExecutionPointerId = e.ExecutionPointerId,
        StepId = e.StepId,
        StepName = Clip(e.StepName, 200),
        Message = Clip(e.Message, 4000),
        Details = e.Details,
    };

    public ActivityEntry ToEntry() =>
        new(Id, Type, Time, InstanceId, DefinitionId, Version, Reference, ExecutionPointerId, StepId, StepName, Message, Details);

    internal static string? Clip(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}

internal sealed class InstanceRow
{
    public string InstanceId { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? Reference { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StatusTime { get; set; }
    public DateTime CreateTime { get; set; }
    public DateTime? CompleteTime { get; set; }
    public int ErrorCount { get; set; }
    public DateTime LastEventTime { get; set; }

    public static InstanceRow From(InstanceIndexEntry e)
    {
        var row = new InstanceRow();
        row.CopyFrom(e);
        return row;
    }

    public void CopyFrom(InstanceIndexEntry e)
    {
        InstanceId = e.InstanceId;
        DefinitionId = ActivityRow.Clip(e.DefinitionId, 200)!;
        Version = e.Version;
        Reference = ActivityRow.Clip(e.Reference, 200);
        Status = e.Status;
        StatusTime = e.StatusTime;
        CreateTime = e.CreateTime;
        CompleteTime = e.CompleteTime;
        ErrorCount = e.ErrorCount;
        LastEventTime = e.LastEventTime;
    }

    public InstanceIndexEntry ToEntry() =>
        new(InstanceId, DefinitionId, Version, Reference, Status, StatusTime, CreateTime, CompleteTime, ErrorCount, LastEventTime);
}

internal sealed class MetadataRow
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
