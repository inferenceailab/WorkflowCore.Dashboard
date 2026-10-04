using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using WorkflowCore.Models.DefinitionStorage.v1;
using WorkflowCore.Primitives;
using WorkflowCore.Services;
using WorkflowCore.Services.DefinitionStorage;

namespace WorkflowCore.Dashboard.Designer;

/// <summary>Reads and writes DSL definitions with Workflow Core's own (Newtonsoft and SharpYaml) deserializers.</summary>
internal static class DslJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    /// <summary>
    /// Accepts JSON or YAML, the two formats Workflow Core's DSL supports. YAML goes through <see cref="SafeYaml"/>
    /// rather than Workflow Core's YAML deserializer, because the text comes from a browser.
    /// </summary>
    public static DefinitionSourceV1 Parse(string text) =>
        Deserializers.Json(text.TrimStart().StartsWith('{') ? text : SafeYaml.ToJson(text));

    public static DefinitionSourceV1 Parse(JsonObject source) => Deserializers.Json(source.ToJsonString());

    /// <summary>Canonical JSON: Workflow Core's property names, enums as names, no nulls.</summary>
    public static JsonObject ToJson(DefinitionSourceV1 definition) =>
        (JsonObject)JsonNode.Parse(JsonConvert.SerializeObject(definition, Settings))!;

    public static string ToText(DefinitionSourceV1 definition) => JsonConvert.SerializeObject(definition, Settings);
}

/// <summary>
/// Checks a definition before it is published: first structural checks that can report every problem at once,
/// then a dry run of Workflow Core's own loader (against a scratch registry) to catch expression errors.
/// </summary>
internal sealed class DefinitionValidator
{
    private readonly IServiceProvider _services;
    private readonly ITypeResolver _types;
    private readonly DesignerOptions _options;
    private readonly Lazy<HashSet<Type>> _allowedSteps;
    private readonly Lazy<HashSet<Type>> _allowedData;

    public DefinitionValidator(IServiceProvider services, ITypeResolver types, StepCatalog catalog, IOptions<DesignerOptions> options)
    {
        _services = services;
        _types = types;
        _options = options.Value;
        _allowedSteps = new(() => Resolve(catalog.Get().Steps.Select(s => s.Type)));
        _allowedData = new(() => Resolve(catalog.Get().DataTypes.Select(d => d.Type)));
    }

    public ValidationResult Validate(JsonObject json)
    {
        DefinitionSourceV1 source;
        try
        {
            source = DslJson.Parse(json);
        }
        catch (Exception ex)
        {
            return Result([new ValidationIssue("error", $"The definition is not valid: {Innermost(ex).Message}", null)]);
        }
        return Validate(source);
    }

    public ValidationResult Validate(DefinitionSourceV1 source)
    {
        var issues = new List<ValidationIssue>();
        void Error(string message, string? stepId = null) => issues.Add(new ValidationIssue("error", message, stepId));
        void Warn(string message, string? stepId = null) => issues.Add(new ValidationIssue("warning", message, stepId));

        if (string.IsNullOrWhiteSpace(source.Id))
            Error("The workflow needs an ID.");

        var dataType = typeof(object);
        if (!string.IsNullOrWhiteSpace(source.DataType))
        {
            if (!TryResolve(source.DataType, out dataType))
                Error($"Data type '{source.DataType}' was not found. Use the form 'Namespace.Type, Assembly'.");
            else if (!_options.AllowAnyType && !_allowedData.Value.Contains(dataType))
                Error($"Data type {dataType.FullName} is not offered by the designer. Add its assembly to DesignerOptions.StepAssemblies.");
        }

        if (source.Steps.Count == 0)
            Error("Add at least one step.");

        var scopes = new List<List<StepSourceV1>>();
        CollectScopes(source.Steps, scopes);
        var all = scopes.SelectMany(s => s).ToList();
        var ids = all.Where(s => !string.IsNullOrWhiteSpace(s.Id)).Select(s => s.Id).ToList();

        foreach (var duplicate in ids.GroupBy(i => i).Where(g => g.Count() > 1))
            Error($"Step ID '{duplicate.Key}' is used more than once.", duplicate.Key);

        foreach (var step in all)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                Error("Every step needs an ID.");
                continue;
            }
            CheckStep(step, dataType, ids, Error, Warn);
        }

        foreach (var scope in scopes)
        {
            foreach (var unreachable in Unreachable(scope))
                Warn("Nothing leads to this step, so it never runs.", unreachable.Id);
        }

        if (issues.All(i => i.Severity != "error"))
        {
            var failure = TryLoad(source);
            if (failure is not null)
            {
                // Workflow Core stops at the first bad expression and rarely names the step.
                // Loading each step on its own finds every faulty step and attributes the error to it.
                var perStep = all.Select(step => (step.Id, Error: TryLoad(Isolate(source, step)))).Where(r => r.Error is not null).ToList();
                if (perStep.Count == 0)
                    Error(failure);
                foreach (var (stepId, message) in perStep)
                    Error(message!, stepId);
            }
        }

        return Result(issues);
    }

    /// <summary>Runs Workflow Core's loader against a scratch registry, so validating never registers anything.</summary>
    private string? TryLoad(DefinitionSourceV1 source)
    {
        try
        {
            new DefinitionLoader(new WorkflowRegistry(_services), _types).LoadDefinition(DslJson.ToText(source), Deserializers.Json);
            return null;
        }
        catch (Exception ex)
        {
            return Innermost(ex).Message;
        }
    }

    /// <summary>A definition containing only a copy of <paramref name="step"/>, without its connections or branches.</summary>
    private static DefinitionSourceV1 Isolate(DefinitionSourceV1 source, StepSourceV1 step)
    {
        var copy = DslJson.Parse(DslJson.ToText(source));
        var single = Deserializers.Json(DslJson.ToText(new DefinitionSourceV1 { Steps = [step] })).Steps[0];
        single.NextStepId = null;
        single.SelectNextStep = new Dictionary<string, string>();
        single.Do = [];
        single.CompensateWith = [];
        copy.Steps = [single];
        return copy;
    }

    private void CheckStep(StepSourceV1 step, Type dataType, List<string> ids, Action<string, string?> error, Action<string, string?> warn)
    {
        if (string.IsNullOrWhiteSpace(step.StepType))
        {
            error("Choose a step type.", step.Id);
            return;
        }
        if (!TryResolve(step.StepType, out var type))
        {
            error($"Step type '{step.StepType}' was not found.", step.Id);
            return;
        }
        if (!_options.AllowAnyType && !_allowedSteps.Value.Contains(type))
        {
            error($"{type.FullName} is not offered by the designer. Add its assembly to DesignerOptions.StepAssemblies.", step.Id);
            return;
        }

        // Most steps are IStepBody types; a few primitives (EndStep) are WorkflowStep subclasses without inputs.
        var bodyType = typeof(IStepBody).IsAssignableFrom(type) ? type : null;
        foreach (var input in (IDictionary<string, object?>)step.Inputs)
        {
            if (bodyType?.GetProperty(input.Key, BindingFlags.Public | BindingFlags.Instance) is not { CanWrite: true })
                error($"{type.Name} has no input '{input.Key}'.", step.Id);
        }

        foreach (var output in step.Outputs)
        {
            var simple = !output.Key.Contains('.') && !output.Key.Contains('[');
            if (simple && dataType != typeof(object) && dataType.GetProperty(output.Key) is null && dataType.GetProperty("Item") is null)
                error($"Data type {dataType.Name} has no property '{output.Key}' to store an output in.", step.Id);
        }

        foreach (var target in Targets(step))
        {
            if (!ids.Contains(target))
                error($"Connects to step '{target}', which does not exist.", step.Id);
        }

        var isContainer = typeof(ContainerStepBody).IsAssignableFrom(type);
        var branchSteps = step.Do?.Sum(b => b.Count) ?? 0;
        if (isContainer && branchSteps == 0)
            warn("This step has no steps inside its branches.", step.Id);
        if (!isContainer && branchSteps > 0)
            warn($"{type.Name} does not run branches, so the steps inside it never run.", step.Id);
    }

    private static void CollectScopes(List<StepSourceV1> steps, List<List<StepSourceV1>> scopes)
    {
        scopes.Add(steps);
        foreach (var step in steps)
        {
            foreach (var branch in step.Do ?? [])
                CollectScopes(branch, scopes);
            if (step.CompensateWith is { Count: > 0 })
                CollectScopes(step.CompensateWith, scopes);
        }
    }

    /// <summary>Within one list of steps, only the first runs on its own; the rest must be connected to.</summary>
    private static IEnumerable<StepSourceV1> Unreachable(List<StepSourceV1> scope)
    {
        if (scope.Count == 0)
            return [];
        var byId = scope.Where(s => s.Id is not null).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<string>();
        var queue = new Queue<StepSourceV1>([scope[0]]);
        while (queue.Count > 0)
        {
            var step = queue.Dequeue();
            if (step.Id is null || !seen.Add(step.Id))
                continue;
            foreach (var target in Targets(step))
            {
                if (byId.TryGetValue(target, out var next))
                    queue.Enqueue(next);
            }
        }
        return scope.Where(s => s.Id is not null && !seen.Contains(s.Id));
    }

    private static IEnumerable<string> Targets(StepSourceV1 step)
    {
        if (!string.IsNullOrWhiteSpace(step.NextStepId))
            yield return step.NextStepId;
        foreach (var key in step.SelectNextStep.Keys)
            yield return key;
    }

    private HashSet<Type> Resolve(IEnumerable<string> names) =>
        names.Select(n => TryResolve(n, out var t) ? t : null).Where(t => t is not null).Cast<Type>().ToHashSet();

    private bool TryResolve(string name, out Type type)
    {
        try
        {
            type = _types.FindType(name);
            return true;
        }
        catch (Exception)
        {
            type = typeof(object);
            return false;
        }
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex is TargetInvocationException or AggregateException && ex.InnerException is not null)
            ex = ex.InnerException!;
        return ex;
    }

    private static ValidationResult Result(List<ValidationIssue> issues) =>
        new(issues.All(i => i.Severity != "error"), issues);
}
