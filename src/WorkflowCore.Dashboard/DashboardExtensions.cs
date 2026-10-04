using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.Api;
using WorkflowCore.Dashboard.Journal;
using WorkflowCore.Dashboard.Live;
using WorkflowCore.Dashboard.Ui;

namespace WorkflowCore.Dashboard;

/// <summary>
/// An add-on that contributes API endpoints under <c>{prefix}/api</c> and a feature flag the UI can check,
/// such as the workflow designer.
/// </summary>
public interface IDashboardExtension
{
    /// <summary>Reported to the UI in the config's <c>features</c> list.</summary>
    string Feature { get; }

    void MapEndpoints(RouteGroupBuilder api);
}

/// <summary>Returned by <c>AddWorkflowCoreDashboard</c> to configure where the activity journal is stored.</summary>
public sealed class DashboardBuilder
{
    internal DashboardBuilder(IServiceCollection services) => Services = services;

    public IServiceCollection Services { get; }

    /// <summary>Replaces the default in-memory journal.</summary>
    public DashboardBuilder UseJournal(Func<IServiceProvider, IDashboardJournal> factory)
    {
        Services.Replace(ServiceDescriptor.Singleton(factory));
        return this;
    }
}

public static class DashboardExtensions
{
    /// <summary>
    /// Registers the dashboard services. Call after <c>AddWorkflow()</c>.
    /// The activity journal is in-memory unless a persistent one is configured on the returned builder.
    /// </summary>
    public static DashboardBuilder AddWorkflowCoreDashboard(this IServiceCollection services, Action<DashboardOptions>? configure = null)
    {
        var options = services.AddOptions<DashboardOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.AddSignalR();
        services.TryAddSingleton<IDashboardJournal, InMemoryJournal>();
        services.TryAddSingleton<JournalWriter>();
        services.AddHostedService(sp => sp.GetRequiredService<JournalWriter>());
        services.AddHostedService<JournalMaintenance>();
        services.AddHostedService<LifeCycleRelay>();
        services.TryAddTransient<InstanceListing>();
        return new DashboardBuilder(services);
    }

    /// <summary>
    /// Maps the dashboard UI, its API (<c>{prefix}/api</c>) and live updates (<c>{prefix}/hub</c>).
    /// The returned group accepts further conventions such as <c>RequireAuthorization()</c>.
    /// </summary>
    public static RouteGroupBuilder MapWorkflowCoreDashboard(this IEndpointRouteBuilder endpoints, string prefix = "/workflows")
    {
        prefix = "/" + prefix.Trim('/');
        if (prefix == "/")
            throw new ArgumentException("The dashboard needs its own path prefix, such as \"/workflows\".", nameof(prefix));

        var group = endpoints.MapGroup(prefix);
        group.AddEndpointFilter(Authorize);
        group.MapHub<DashboardHub>("/hub");

        var api = group.MapGroup("/api");
        api.AddEndpointFilter(DashboardApi.RequireActions);
        DashboardApi.Map(api);
        foreach (var extension in endpoints.ServiceProvider.GetServices<IDashboardExtension>())
            extension.MapEndpoints(api);

        EmbeddedUi.Map(group, prefix);
        return group;
    }

    private static ValueTask<object?> Authorize(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetRequiredService<IOptions<DashboardOptions>>().Value;
        return options.Authorization(http)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));
    }
}
