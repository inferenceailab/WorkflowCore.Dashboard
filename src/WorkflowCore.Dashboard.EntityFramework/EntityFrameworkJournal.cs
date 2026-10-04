using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using WorkflowCore.Dashboard.Journal;

namespace WorkflowCore.Dashboard.EntityFramework;

/// <summary>
/// Journal stored in any EF Core relational database. Its three tables (prefixed <c>WfcDashboard_</c>)
/// are created on first use and can share a database with Workflow Core's own tables.
/// </summary>
internal sealed class EntityFrameworkJournal : IDashboardJournal
{
    private const int ChunkSize = 500;

    private readonly DbContextOptions<JournalDbContext> _options;
    private readonly string? _schema;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    public EntityFrameworkJournal(DbContextOptions<JournalDbContext> options, string? schema)
    {
        _options = options;
        _schema = schema;
        using var db = CreateContext();
        Name = $"Entity Framework ({db.Database.ProviderName?.Split('.').Last() ?? "unknown"})";
    }

    public string Name { get; }

    public bool IsPersistent => true;

    public async Task AppendAsync(IReadOnlyList<ActivityEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return;
        await EnsureSchema(cancellationToken);

        // The same ID twice in one batch is an error event re-sent with its stack trace: keep the complete one.
        var unique = entries
            .GroupBy(e => e.Id)
            .Select(g => g.FirstOrDefault(e => e.Details is not null) ?? g.First())
            .ToList();

        try
        {
            await Write(unique, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another node stored some of these events first. A second pass sees them and skips them.
            await Write(unique, cancellationToken);
        }
    }

    private async Task Write(List<ActivityEntry> entries, CancellationToken ct)
    {
        await using var db = CreateContext();

        var ids = entries.Select(e => e.Id).ToList();
        var existing = await db.Activity.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var added = new List<ActivityEntry>();
        foreach (var entry in entries)
        {
            if (existing.TryGetValue(entry.Id, out var row))
            {
                if (row.Details is null && entry.Details is not null)
                    row.Details = entry.Details;
                continue;
            }
            db.Activity.Add(ActivityRow.From(entry));
            added.Add(entry);
        }

        var changes = InstanceIndexReducer.Reduce(added);
        if (changes.Count > 0)
        {
            var instanceIds = changes.Select(c => c.InstanceId).ToList();
            var rows = await db.Instances.Where(r => instanceIds.Contains(r.InstanceId)).ToDictionaryAsync(r => r.InstanceId, ct);
            foreach (var change in changes)
            {
                if (rows.TryGetValue(change.InstanceId, out var row))
                    row.CopyFrom(InstanceIndexReducer.Apply(row.ToEntry(), change));
                else
                    db.Instances.Add(InstanceRow.From(InstanceIndexReducer.Apply(null, change)));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ActivityEntry>> GetActivityAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();

        var q = db.Activity.AsNoTracking();
        if (!string.IsNullOrEmpty(query.InstanceId))
            q = q.Where(a => a.InstanceId == query.InstanceId);
        if (query.Before is { } before)
            q = q.Where(a => a.Time < before);
        if (!query.IncludeSteps)
            q = q.Where(a => a.Type != "StepStarted" && a.Type != "StepCompleted");

        var rows = await q.OrderByDescending(a => a.Time).ThenByDescending(a => a.Id).Take(query.Take).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToEntry()).ToList();
    }

    public async Task<ActivityTotals> GetTotalsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();

        var counts = await db.Activity
            .Where(a => a.Time >= since)
            .GroupBy(a => a.Type)
            .Select(g => new { g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken);
        return new ActivityTotals(since, counts.ToDictionary(c => c.Key, c => c.Count));
    }

    public async Task<InstanceIndexPage> QueryInstancesAsync(InstanceIndexQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();

        var q = db.Instances.AsNoTracking();
        if (query.Status is not null)
            q = q.Where(r => r.Status == query.Status);
        if (query.DefinitionId is not null)
            q = q.Where(r => r.DefinitionId == query.DefinitionId);
        if (query.CreatedFrom is { } from)
            q = q.Where(r => r.CreateTime >= from);
        if (query.CreatedTo is { } to)
            q = q.Where(r => r.CreateTime <= to);

        var total = await q.LongCountAsync(cancellationToken);
        var rows = await q
            .OrderByDescending(r => r.CreateTime)
            .ThenBy(r => r.InstanceId)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
        return new InstanceIndexPage(rows.Select(r => r.ToEntry()).ToList(), total);
    }

    public async Task AddMissingInstancesAsync(IReadOnlyList<InstanceIndexEntry> rows, CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return;
        await EnsureSchema(cancellationToken);

        foreach (var chunk in rows.DistinctBy(r => r.InstanceId).Chunk(ChunkSize))
        {
            await using var db = CreateContext();
            var ids = chunk.Select(r => r.InstanceId).ToList();
            var known = (await db.Instances.Where(r => ids.Contains(r.InstanceId)).Select(r => r.InstanceId).ToListAsync(cancellationToken))
                .ToHashSet();
            db.Instances.AddRange(chunk.Where(r => !known.Contains(r.InstanceId)).Select(InstanceRow.From));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveInstancesAsync(IReadOnlyCollection<string> instanceIds, CancellationToken cancellationToken = default)
    {
        if (instanceIds.Count == 0)
            return;
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();

        var ids = instanceIds.ToList();
        db.Instances.RemoveRange(await db.Instances.Where(r => ids.Contains(r.InstanceId)).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PurgeAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();

        // ExecuteDelete moved between EF Core 8 and 9, so a build against one fails on the other.
        // Plain SQL with provider-quoted names works on every version and provider.
        var sql = db.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier("WfcDashboard_Activity", _schema);
        var time = sql.DelimitIdentifier(nameof(ActivityRow.Time));
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM {table} WHERE {time} < {{0}}", [cutoff.ToUniversalTime()], cancellationToken);
    }

    public async Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();
        return (await db.Metadata.AsNoTracking().FirstOrDefaultAsync(m => m.Key == key, cancellationToken))?.Value;
    }

    public async Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();
        var row = await db.Metadata.FirstOrDefaultAsync(m => m.Key == key, cancellationToken);
        if (row is null)
            db.Metadata.Add(new MetadataRow { Key = key, Value = value });
        else
            row.Value = value;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> ListMetadataAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();
        var rows = await db.Metadata.AsNoTracking().Where(m => m.Key.StartsWith(prefix)).ToListAsync(cancellationToken);
        // Some databases compare case-insensitively; keep exact prefix matches only.
        return rows.Where(m => m.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(m => m.Key, m => m.Value);
    }

    public async Task DeleteMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureSchema(cancellationToken);
        await using var db = CreateContext();
        var row = await db.Metadata.FirstOrDefaultAsync(m => m.Key == key, cancellationToken);
        if (row is null)
            return;
        db.Metadata.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the database and the dashboard tables when missing. <c>EnsureCreated</c> is not enough because it
    /// does nothing in a database that already has tables, such as the one Workflow Core uses.
    /// </summary>
    private async Task EnsureSchema(CancellationToken ct)
    {
        if (_schemaReady)
            return;

        await _schemaGate.WaitAsync(ct);
        try
        {
            if (_schemaReady)
                return;

            await using var db = CreateContext();
            var creator = db.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(ct))
                await creator.CreateAsync(ct);
            if (!await TablesExist(db, ct))
                await creator.CreateTablesAsync(ct);
            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private static async Task<bool> TablesExist(JournalDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Metadata.AnyAsync(ct);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }

    private JournalDbContext CreateContext() => new(_options, _schema);
}
