using System.Text.Json.Nodes;

namespace WorkflowCore.Dashboard.Designer;

/// <param name="Kind">string, bool, number, timespan, datetime, collection or object; the UI uses it for input hints.</param>
public sealed record CatalogProperty(string Name, string Type, string Kind);

/// <param name="Type">The name to use as <c>StepType</c> in a definition, e.g. <c>MyApp.Steps.SendEmail, MyApp</c>.</param>
/// <param name="MultipleBranches">True for steps whose branches run side by side (Parallel).</param>
public sealed record StepTypeInfo(
    string Type,
    string Name,
    string Category,
    string? Description,
    bool IsContainer,
    bool MultipleBranches,
    IReadOnlyList<CatalogProperty> Inputs,
    IReadOnlyList<CatalogProperty> Outputs);

public sealed record DataTypeInfo(string Type, string Name, string? Namespace, IReadOnlyList<CatalogProperty> Properties);

public sealed record StepCatalogResponse(IReadOnlyList<StepTypeInfo> Steps, IReadOnlyList<DataTypeInfo> DataTypes);

/// <summary>
/// Everything the designer stores for one workflow ID: an optional draft and every published version.
/// <c>Source</c> is a Workflow Core DSL definition (the JSON format of <c>DefinitionSourceV1</c>);
/// <c>Layout</c> holds node positions, which the DSL format has no place for.
/// </summary>
public sealed class DesignerDocument
{
    public string Id { get; set; } = string.Empty;
    public DesignerDraft? Draft { get; set; }
    public List<DesignerVersion> Versions { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

public sealed record DesignerDraft(JsonObject Source, JsonObject? Layout, DateTime SavedAt);

public sealed record DesignerVersion(int Version, JsonObject Source, JsonObject? Layout, DateTime PublishedAt);

public sealed record DesignerSummary(string Id, string? Description, bool HasDraft, int? LatestVersion, DateTime UpdatedAt);

public sealed record SaveDesignRequest(JsonObject Source, JsonObject? Layout);

public sealed record ValidateRequest(JsonObject Source);

/// <param name="Severity">"error" blocks publishing; "warning" does not.</param>
/// <param name="StepId">The DSL step ID the issue is about, when known.</param>
public sealed record ValidationIssue(string Severity, string Message, string? StepId);

public sealed record ValidationResult(bool Valid, IReadOnlyList<ValidationIssue> Issues);

public sealed record PublishResponse(int Version, ValidationResult Validation);

public sealed record ImportRequest(string Text);

public sealed record ImportResponse(JsonObject Source);
