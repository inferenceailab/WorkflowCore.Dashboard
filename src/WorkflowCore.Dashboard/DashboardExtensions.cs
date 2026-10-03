using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.Api;
using WorkflowCore.Dashboard.Live;
using WorkflowCore.Dashboard.Ui;

namespace WorkflowCore.Dashboard;

public static class DashboardExtensions
{
    /// <summary>
    /// Registers the dashboard services. Call after <c>AddWorkflow()</c>.
    /// </summary>
    public static IServiceCollection AddWorkflowCoreDashboard(this IServiceCollection services, Action<DashboardOptions>? configure = null)
    {
        var options = services.AddOptions<DashboardOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.AddSignalR();
        services.TryAddSingleton<ActivityFeed>();
        services.AddHostedService<LifeCycleRelay>();
        return services;
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
