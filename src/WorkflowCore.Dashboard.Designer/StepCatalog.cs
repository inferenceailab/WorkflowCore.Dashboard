using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using WorkflowCore.Primitives;

namespace WorkflowCore.Dashboard.Designer;

/// <summary>
/// The step types and data types the designer offers, found by reflection over the app's assemblies
/// plus Workflow Core's built-in primitives that work in JSON/YAML definitions.
/// </summary>
internal sealed class StepCatalog
{
    private const int MaxDataTypes = 500;

    private static readonly (Type Type, string Name, string Category, string Description)[] Primitives =
    [
        (typeof(If), "If", "Control flow", "Runs its branch when Condition is true, then continues."),
        (typeof(While), "While", "Control flow", "Repeats its branch while Condition is true."),
        (typeof(Foreach), "For each", "Control flow", "Runs its branch for every item in Collection. Inside the branch, context.Item is the current item."),
        (typeof(Sequence), "Parallel", "Control flow", "Runs its branches side by side and continues when all of them finish."),
        (typeof(Decide), "Decide", "Control flow", "Evaluates Expression; conditions on outgoing connections can test it as outcome."),
        (typeof(EndStep), "End", "Control flow", "Ends the workflow."),
        (typeof(Delay), "Delay", "Timing", "Waits for Period (a TimeSpan, e.g. TimeSpan.FromMinutes(5))."),
        (typeof(Schedule), "Schedule", "Timing", "Runs its branch once after Interval."),
        (typeof(Recur), "Recur", "Timing", "Runs its branch every Interval until StopCondition is true."),
        (typeof(WaitFor), "Wait for event", "Events", "Pauses until an event with EventName and EventKey is published. EventData holds its payload."),
        (typeof(Activity), "Activity", "Events", "Waits for an external worker to complete ActivityName. Result holds its answer."),
    ];

    private readonly DesignerOptions _options;
    private readonly IWorkflowRegistry _registry;
    private readonly Lazy<StepCatalogResponse> _catalog;

    public StepCatalog(IOptions<DesignerOptions> options, IWorkflowRegistry registry)
    {
        _options = options.Value;
        _registry = registry;
        _catalog = new Lazy<StepCatalogResponse>(Build);
    }

    public StepCatalogResponse Get() => _catalog.Value;

    /// <summary><c>Namespace.Type, Assembly</c>: the form Workflow Core's DSL type resolver accepts.</summary>
    public static string DslTypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

    private StepCatalogResponse Build()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(_options.IncludeAssembly)
            .Concat(_options.StepAssemblies)
            .Distinct()
            .ToList();
        var types = assemblies.SelectMany(LoadableTypes).Where(IsPublicConcrete).ToList();

        var steps = Primitives
            .Select(p => Describe(p.Type, p.Name, p.Category, p.Description))
            .Concat(types
                .Where(t => typeof(IStepBody).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .Select(t => Describe(t, Humanize(t.Name), CategoryOf(t), t.GetCustomAttribute<DescriptionAttribute>()?.Description)))
            .ToList();

        // Data types already used by registered workflows come first, then candidates from the app's assemblies.
        var used = _registry.GetAllDefinitions().Select(d => d.DataType).Where(t => t is not null && t != typeof(object)).Cast<Type>();
        var candidates = types.Where(IsDataTypeCandidate).OrderBy(t => t.Name);
        var dataTypes = used.Concat(candidates)
            .Distinct()
            .Take(MaxDataTypes)
            .Select(t => new DataTypeInfo(DslTypeName(t), t.Name, t.Namespace, ReadableProperties(t)))
            .ToList();

        return new StepCatalogResponse(steps, dataTypes);
    }

    private static StepTypeInfo Describe(Type type, string name, string category, string? description)
    {
        var isContainer = typeof(ContainerStepBody).IsAssignableFrom(type);
        var inputs = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0 && IsStepProperty(p))
            .Select(Property)
            .ToList();
        var outputs = ReadableProperties(type).Where(p => type.GetProperty(p.Name) is { } prop && IsStepProperty(prop)).ToList();

        return new StepTypeInfo(DslTypeName(type), name, category, description, isContainer, type == typeof(Sequence), inputs, outputs);
    }

    private static List<CatalogProperty> ReadableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .Select(Property)
            .ToList();

    // Properties of Workflow Core's base classes are plumbing, not inputs.
    private static bool IsStepProperty(PropertyInfo p) =>
        p.DeclaringType is { } d && d != typeof(StepBody) && d != typeof(StepBodyAsync) && d != typeof(ContainerStepBody) && d != typeof(WorkflowStep);

    private static CatalogProperty Property(PropertyInfo p) => new(p.Name, FriendlyName(p.PropertyType), KindOf(p.PropertyType));

    private static string KindOf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(TimeSpan)) return "timespan";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "datetime";
        if (type.IsPrimitive || type == typeof(decimal)) return "number";
        if (typeof(IEnumerable).IsAssignableFrom(type)) return "collection";
        return "object";
    }

    private static string FriendlyName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return FriendlyName(inner) + "?";
        if (!type.IsGenericType)
            return type.Name;
        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>";
    }

    private static string CategoryOf(Type type)
    {
        var last = type.Namespace?.Split('.').LastOrDefault();
        return string.IsNullOrEmpty(last) ? "Steps" : last;
    }

    /// <summary>"SendEmailStep" becomes "Send email step".</summary>
    private static string Humanize(string name)
    {
        var chars = new List<char>(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                chars.Add(' ');
                chars.Add(char.ToLowerInvariant(name[i]));
            }
            else
            {
                chars.Add(name[i]);
            }
        }
        return new string(chars.ToArray());
    }

    private static bool IsPublicConcrete(Type t) =>
        t.IsClass && !t.IsAbstract && (t.IsPublic || t.IsNestedPublic) && !t.ContainsGenericParameters
        && !t.IsDefined(typeof(CompilerGeneratedAttribute), false);

    private static bool IsDataTypeCandidate(Type t) =>
        !typeof(IStepBody).IsAssignableFrom(t)
        && !typeof(WorkflowStep).IsAssignableFrom(t)
        && !t.GetInterfaces().Any(i => i.IsGenericType ? i.GetGenericTypeDefinition() == typeof(IWorkflow<>) : i == typeof(IWorkflow))
        && t.GetConstructor(Type.EmptyTypes) is not null
        && t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.SetMethod is { IsPublic: true });

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
