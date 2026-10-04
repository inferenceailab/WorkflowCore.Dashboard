using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Api;

internal static class StepNames
{
    public static string For(WorkflowStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.Name))
            return step.Name;
        if (!string.IsNullOrWhiteSpace(step.ExternalId))
            return step.ExternalId;
        // EndStep and other WorkflowStep subclasses have no body type.
        return TypeName(step.BodyType ?? step.GetType());
    }

    public static string? For(IWorkflowRegistry registry, string definitionId, int version, int stepId)
    {
        var step = registry.GetDefinition(definitionId, version)?.Steps.FindById(stepId);
        return step is null ? null : For(step);
    }

    /// <summary>Readable type name, e.g. <c>Foreach</c> or <c>ActionStepBody&lt;OrderData&gt;</c>.</summary>
    public static string TypeName(Type? type)
    {
        if (type is null)
            return "?";
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick > 0)
            name = name[..tick];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }
}
