using Microsoft.AspNetCore.Authorization;
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

    /// <summary>Endpoints directly under the dashboard prefix, outside <c>/api</c>, such as sign-out pages.</summary>
    void MapDashboardEndpoints(RouteGroupBuilder dashboard)
    {
    }
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
        services.TryAddSingleton<DashboardRoute>();
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

        var services = endpoints.ServiceProvider;
        services.GetRequiredService<DashboardRoute>().Prefix = prefix;
        var options = services.GetRequiredService<IOptions<DashboardOptions>>().Value;

        var group = endpoints.MapGroup(prefix);
        if (options.AuthorizationPolicy is { } policy)
            group.RequireAuthorization(policy);
        group.AddEndpointFilter(Authorize);
        group.MapHub<DashboardHub>("/hub");

        var api = group.MapGroup("/api");
        api.AddEndpointFilter(DashboardApi.RequireActions);
        DashboardApi.Map(api);
        foreach (var extension in services.GetServices<IDashboardExtension>())
        {
            extension.MapEndpoints(api);
            extension.MapDashboardEndpoints(group);
        }

        EmbeddedUi.Map(group, prefix);
        return group;
    }

    private static ValueTask<object?> Authorize(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        // Pages such as "signed out" must work for everyone.
        if (http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return next(context);

        var options = http.RequestServices.GetRequiredService<IOptions<DashboardOptions>>().Value;

        var headers = http.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        // No framing by other sites unless configured: the dashboard has buttons worth clickjacking.
        headers.ContentSecurityPolicy = $"frame-ancestors {options.FrameAncestors ?? "'none'"}";
        if (options.FrameAncestors is null)
            headers.XFrameOptions = "DENY";

        if (options.Authorization(http))
            return next(context);

        // A signed-in user without access opening the UI gets a page saying who they are and why, not a bare 403.
        return ValueTask.FromResult<object?>(http.User.Identity?.IsAuthenticated == true && AccessDeniedPage.IsPageRequest(http)
            ? AccessDeniedPage.Render(http, options)
            : Results.StatusCode(StatusCodes.Status403Forbidden));
    }
}
