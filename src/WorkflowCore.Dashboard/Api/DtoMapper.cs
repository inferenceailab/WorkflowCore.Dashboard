using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Api;

internal static class DtoMapper
{
    // Workflow data, outcomes and event payloads are arbitrary CLR objects. Workflow Core itself
    // persists them with Newtonsoft, so the same serializer is used to show them.
    private static readonly JsonSerializerSettings DataSettings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        MaxDepth = 32,
    };

    public static DefinitionSummary ToSummary(WorkflowDefinition def, bool isLatest) => new(
        def.Id,
        def.Version,
        def.Description,
        def.DataType?.FullName,
        def.Steps.Count,
        isLatest);

    public static DefinitionDetail ToDetail(WorkflowDefinition def) => new(
        def.Id,
        def.Version,
        def.Description,
        def.DataType?.FullName,
        def.DefaultErrorBehavior.ToString(),
        def.DefaultErrorRetryInterval?.ToString(),
        DataTemplate(def.DataType),
        def.Steps.Select(ToDto).ToList());

    private static StepDto ToDto(WorkflowStep step) => new(
        step.Id,
        step.ExternalId,
        StepNames.For(step),
        StepNames.TypeName(step.BodyType),
        step.BodyType.FullName ?? step.BodyType.Name,
        step.Children.ToList(),
        step.Outcomes.Select(o => new OutcomeDto(o.NextStep, o.Label, o.ExternalNextStepId)).ToList(),
        step.ErrorBehavior?.ToString(),
        step.RetryInterval?.ToString(),
        step.CompensationStepId);

    public static InstanceSummary ToSummary(WorkflowInstance wf, WorkflowDefinition? def)
    {
        // A step waiting for an event is not "active" in Workflow Core, but it is where the workflow currently is.
        var active = wf.ExecutionPointers
            .Where(p => p.Active || p.Status == PointerStatus.WaitingForEvent)
            .OrderBy(p => p.StartTime ?? DateTime.MaxValue)
            .ToList();
        var current = active.Count switch
        {
            0 => null,
            1 => PointerName(active[0], def),
            _ => $"{PointerName(active[0], def)} +{active.Count - 1}",
        };

        return new InstanceSummary(
            wf.Id,
            wf.WorkflowDefinitionId,
            wf.Version,
            wf.Description,
            wf.Reference,
            wf.Status.ToString(),
            Utc(wf.CreateTime),
            Utc(wf.CompleteTime),
            NextExecution(wf.NextExecution),
            active.Count,
            wf.ExecutionPointers.Count(p => p.Status == PointerStatus.Failed),
            current);
    }

    public static InstanceDetail ToDetail(WorkflowInstance wf, WorkflowDefinition? def) => new(
        ToSummary(wf, def),
        ToJson(wf.Data),
        wf.ExecutionPointers
            .OrderBy(p => p.StartTime ?? DateTime.MaxValue)
            .Select(p => ToDto(p, def))
            .ToList());

    private static PointerDto ToDto(ExecutionPointer p, WorkflowDefinition? def)
    {
        var step = def?.Steps.FindById(p.StepId);
        return new PointerDto(
            p.Id,
            p.StepId,
            PointerName(p, def),
            step is null ? null : StepNames.TypeName(step.BodyType),
            p.Status.ToString(),
            p.Active,
            Utc(p.StartTime),
            Utc(p.EndTime),
            Utc(p.SleepUntil),
            p.RetryCount,
            p.EventName,
            p.EventKey,
            p.EventPublished,
            p.PredecessorId,
            p.Children.ToList(),
            p.Scope.ToList(),
            ToJson(p.Outcome),
            ToJson(p.EventData),
            ToJson(p.PersistenceData),
            ToJson(p.ContextItem));
    }

    private static string PointerName(ExecutionPointer p, WorkflowDefinition? def)
    {
        var step = def?.Steps.FindById(p.StepId);
        if (step is not null)
            return StepNames.For(step);
        return string.IsNullOrWhiteSpace(p.StepName) ? $"Step {p.StepId}" : p.StepName;
    }

    public static JsonNode? ToJson(object? value)
    {
        if (value is null)
            return null;
        try
        {
            return JsonNode.Parse(JsonConvert.SerializeObject(value, DataSettings));
        }
        catch (Exception ex)
        {
            return JsonValue.Create($"<could not serialize {value.GetType().Name}: {ex.Message}>");
        }
    }

    /// <summary>A default instance of the workflow data type, used to prefill the start dialog.</summary>
    private static JsonNode? DataTemplate(Type? dataType)
    {
        if (dataType is null || dataType == typeof(object) || dataType.GetConstructor(Type.EmptyTypes) is null)
            return null;
        try
        {
            return ToJson(Activator.CreateInstance(dataType));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts request JSON into the workflow's data type. Untyped workflows get an <see cref="ExpandoObject"/>.
    /// </summary>
    public static object? ToWorkflowData(JsonElement? json, Type? dataType)
    {
        if (json is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var target = dataType is null || dataType == typeof(object) ? typeof(ExpandoObject) : dataType;
        return JsonConvert.DeserializeObject(element.GetRawText(), target);
    }

    /// <summary>Event payloads have no declared type: primitives stay primitives, objects become ExpandoObject.</summary>
    public static object? ToEventData(JsonElement? json)
    {
        if (json is not { } element)
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array => JArray.Parse(element.GetRawText()).ToObject<List<object>>(),
            _ => JsonConvert.DeserializeObject<ExpandoObject>(element.GetRawText()),
        };
    }

    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    // Workflow Core stores the next run as UTC ticks; 0 means "as soon as possible".
    private static DateTime? NextExecution(long? ticks) => ticks switch
    {
        null => null,
        0 => DateTime.UtcNow,
        var t => new DateTime(t.Value, DateTimeKind.Utc),
    };
}
