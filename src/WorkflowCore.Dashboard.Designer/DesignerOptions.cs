using System.Reflection;

namespace WorkflowCore.Dashboard.Designer;

public class DesignerOptions
{
    /// <summary>
    /// Decides which loaded assemblies are scanned for step types and data types. By default: every assembly that
    /// references Workflow Core, except Workflow Core's own packages and the dashboard.
    /// </summary>
    public Func<Assembly, bool> IncludeAssembly { get; set; } = DefaultIncludeAssembly;

    /// <summary>
    /// Assemblies to scan even if they are not loaded yet when the catalog is first built, for example a library
    /// whose steps are only used from JSON/YAML definitions.
    /// </summary>
    public List<Assembly> StepAssemblies { get; } = new();

    /// <summary>How often each node checks the store for versions published on other nodes.</summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(30);

    private static readonly string[] ExcludedPrefixes =
    [
        "WorkflowCore.Persistence.",
        "WorkflowCore.Providers.",
        "WorkflowCore.QueueProviders.",
        "WorkflowCore.LockProviders.",
    ];

    private static readonly HashSet<string> ExcludedNames =
    [
        "WorkflowCore",
        "WorkflowCore.DSL",
        "WorkflowCore.Users",
        "WorkflowCore.Testing",
        "WorkflowCore.Dashboard",
        "WorkflowCore.Dashboard.EntityFramework",
        "WorkflowCore.Dashboard.Designer",
    ];

    public static bool DefaultIncludeAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;
        if (assembly.IsDynamic || ExcludedNames.Contains(name) || ExcludedPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            return false;

        // Step bodies implement IStepBody, so any assembly that defines them references Workflow Core.
        return assembly.GetReferencedAssemblies().Any(a => a.Name == "WorkflowCore");
    }
}
