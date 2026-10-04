using System.Text.Json;
using WorkflowCore.Dashboard.Journal;

namespace WorkflowCore.Dashboard.Designer;

/// <summary>
/// Keeps designer documents in the dashboard journal's metadata, so they live wherever the journal does:
/// in memory by default, or in the database with a persistent journal.
/// </summary>
internal sealed class DesignerStore
{
    private const string Prefix = "designer:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDashboardJournal _journal;

    public DesignerStore(IDashboardJournal journal)
    {
        _journal = journal;
    }

    public async Task<IReadOnlyList<DesignerDocument>> ListAsync(CancellationToken ct = default)
    {
        var entries = await _journal.ListMetadataAsync(Prefix, ct);
        return entries.Values
            .Select(v => JsonSerializer.Deserialize<DesignerDocument>(v, Json))
            .Where(d => d is not null)
            .Cast<DesignerDocument>()
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DesignerDocument?> GetAsync(string id, CancellationToken ct = default)
    {
        var json = await _journal.GetMetadataAsync(Prefix + id, ct);
        return json is null ? null : JsonSerializer.Deserialize<DesignerDocument>(json, Json);
    }

    public Task SaveAsync(DesignerDocument document, CancellationToken ct = default)
    {
        document.UpdatedAt = DateTime.UtcNow;
        return _journal.SetMetadataAsync(Prefix + document.Id, JsonSerializer.Serialize(document, Json), ct);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) => _journal.DeleteMetadataAsync(Prefix + id, ct);
}
