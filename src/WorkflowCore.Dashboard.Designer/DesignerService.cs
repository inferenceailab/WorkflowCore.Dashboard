using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkflowCore.Interface;
using WorkflowCore.Services.DefinitionStorage;

namespace WorkflowCore.Dashboard.Designer;

public sealed class DesignerException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Drafts, publishing and registration of designer-made definitions.</summary>
internal sealed partial class DesignerService
{
    private static readonly SemaphoreSlim PublishGate = new(1, 1);

    private readonly DesignerStore _store;
    private readonly DefinitionValidator _validator;
    private readonly IWorkflowRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<DesignerService> _logger;

    public DesignerService(
        DesignerStore store, DefinitionValidator validator, IWorkflowRegistry registry, IServiceProvider services, ILogger<DesignerService> logger)
    {
        _store = store;
        _validator = validator;
        _registry = registry;
        _services = services;
        _logger = logger;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.\-]{0,59}$")]
    private static partial Regex IdPattern();

    public static void EnsureValidId(string id)
    {
        if (!IdPattern().IsMatch(id))
            throw new DesignerException("invalid-id", "Use 1–60 letters, digits, '.', '-' or '_', starting with a letter.");
    }

    public async Task<IReadOnlyList<DesignerSummary>> List(CancellationToken ct)
    {
        var documents = await _store.ListAsync(ct);
        return documents.Select(d =>
        {
            var latest = d.Versions.MaxBy(v => v.Version);
            var source = d.Draft?.Source ?? latest?.Source;
            return new DesignerSummary(d.Id, source?["Description"]?.GetValue<string>(), d.Draft is not null, latest?.Version, d.UpdatedAt);
        }).ToList();
    }

    public Task<DesignerDocument?> Get(string id, CancellationToken ct) => _store.GetAsync(id, ct);

    public async Task<DesignerDocument> SaveDraft(string id, SaveDesignRequest request, CancellationToken ct)
    {
        EnsureValidId(id);
        var document = await _store.GetAsync(id, ct) ?? new DesignerDocument { Id = id };
        var source = (JsonObject)request.Source.DeepClone();
        source["Id"] = id;
        document.Draft = new DesignerDraft(source, request.Layout, DateTime.UtcNow);
        await _store.SaveAsync(document, ct);
        return document;
    }

    /// <summary>Discards the draft. A workflow that was never published disappears entirely.</summary>
    public async Task DiscardDraft(string id, CancellationToken ct)
    {
        var document = await _store.GetAsync(id, ct);
        if (document is null)
            return;
        if (document.Versions.Count == 0)
        {
            await _store.DeleteAsync(id, ct);
            return;
        }
        document.Draft = null;
        await _store.SaveAsync(document, ct);
    }

    public ValidationResult Validate(JsonObject source) => _validator.Validate(source);

    /// <summary>
    /// Validates, gives the definition the next free version number, registers it with Workflow Core
    /// so it can be started at once, and stores it so every node loads it on startup.
    /// </summary>
    public async Task<PublishResponse> Publish(string id, SaveDesignRequest request, CancellationToken ct)
    {
        EnsureValidId(id);
        var source = (JsonObject)request.Source.DeepClone();
        source["Id"] = id;

        await PublishGate.WaitAsync(ct);
        try
        {
            var document = await _store.GetAsync(id, ct) ?? new DesignerDocument { Id = id };
            var registered = _registry.GetAllDefinitions().Where(d => d.Id == id).Select(d => d.Version);
            var stored = document.Versions.Select(v => v.Version);
            var version = registered.Concat(stored).DefaultIfEmpty(0).Max() + 1;
            source["Version"] = version;

            var validation = _validator.Validate(source);
            if (!validation.Valid)
                return new PublishResponse(0, validation);

            var canonical = DslJson.ToJson(DslJson.Parse(source));
            Register(canonical);

            document.Versions.Add(new DesignerVersion(version, canonical, request.Layout, DateTime.UtcNow));
            document.Draft = null;
            await _store.SaveAsync(document, ct);
            _logger.LogInformation("Published workflow {Id} version {Version} from the designer", id, version);
            return new PublishResponse(version, validation);
        }
        finally
        {
            PublishGate.Release();
        }
    }

    private const int MaxImportLength = 1_000_000;

    public static ImportResponse Import(string text)
    {
        if (text.Length > MaxImportLength)
            throw new DesignerException("too-large", "Definitions larger than 1 MB cannot be imported.");
        try
        {
            return new ImportResponse(DslJson.ToJson(DslJson.Parse(text)));
        }
        catch (DesignerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DesignerException("invalid-definition", $"Could not read the definition: {ex.Message}");
        }
    }

    /// <summary>Registers every stored version that this node does not know yet. Returns how many were added.</summary>
    public async Task<int> RegisterStoredVersions(CancellationToken ct)
    {
        var added = 0;
        foreach (var document in await _store.ListAsync(ct))
        {
            foreach (var version in document.Versions.OrderBy(v => v.Version))
            {
                if (_registry.IsRegistered(document.Id, version.Version))
                    continue;
                try
                {
                    Register(version.Source);
                    added++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not register designer workflow {Id} version {Version}", document.Id, version.Version);
                }
            }
        }
        return added;
    }

    private void Register(JsonObject source)
    {
        var loader = _services.GetRequiredService<IDefinitionLoader>();
        loader.LoadDefinition(source.ToJsonString(), Deserializers.Json);
    }
}
