using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.Live;
using WorkflowCore.Exceptions;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Api;

internal static class DashboardApi
{
    // The dashboard uses its own JSON settings so the host app's settings cannot change the wire format.
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/config", GetConfig);
        api.MapGet("/definitions", GetDefinitions);
        api.MapGet("/definitions/{id}/{version:int}", GetDefinition);
        api.MapGet("/instances", GetInstances);
        api.MapGet("/instances/{id}", GetInstance);
        api.MapGet("/activity", GetActivity);
        api.MapGet("/activity/totals", GetActivityTotals);

        api.MapPost("/instances", StartWorkflow);
        api.MapPost("/instances/{id}/suspend", (string id, IWorkflowController c) => Act(() => c.SuspendWorkflow(id)));
        api.MapPost("/instances/{id}/resume", (string id, IWorkflowController c) => Act(() => c.ResumeWorkflow(id)));
        api.MapPost("/instances/{id}/terminate", (string id, IWorkflowController c) => Act(() => c.TerminateWorkflow(id)));
        api.MapPost("/events", PublishEvent);
    }

    private static IResult GetConfig(IOptions<DashboardOptions> options, IPersistenceProvider store, ActivityFeed feed) =>
        Ok(new DashboardConfig(options.Value.Title, options.Value.AllowActions, store.GetType().Name, feed.StartedAt));

    private static IResult GetDefinitions(IWorkflowRegistry registry)
    {
        var all = registry.GetAllDefinitions().ToList();
        var latest = all.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.Max(d => d.Version));
        var result = all
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(d => d.Version)
            .Select(d => DtoMapper.ToSummary(d, latest[d.Id] == d.Version))
            .ToList();
        return Ok(result);
    }

    private static IResult GetDefinition(string id, int version, IWorkflowRegistry registry)
    {
        var def = registry.GetDefinition(id, version);
        return def is null
            ? Error(StatusCodes.Status404NotFound, "definition-not-found", $"Workflow '{id}' version {version} is not registered.")
            : Ok(DtoMapper.ToDetail(def));
    }

    private static async Task<IResult> GetInstances(
        HttpRequest request, IPersistenceProvider store, IWorkflowRegistry registry, IOptions<DashboardOptions> options)
    {
        var q = request.Query;
        if (!TryParseStatus(q["status"], out var status))
            return Error(StatusCodes.Status400BadRequest, "invalid-status", $"Unknown status '{q["status"]}'.");

        var skip = Math.Max(0, ParseInt(q["skip"]) ?? 0);
        var take = Math.Clamp(ParseInt(q["take"]) ?? 25, 1, options.Value.MaxPageSize);
        var definitionId = NullIfEmpty(q["definitionId"]);
        var from = ParseDate(q["createdFrom"]);
        var to = ParseDate(q["createdTo"]);

        List<WorkflowInstance> page;
        try
        {
            // Obsolete in Workflow Core, but it is the only provider-agnostic listing API.
            // One extra row is requested to tell whether a next page exists.
#pragma warning disable CS0612, CS0618
            page = (await store.GetWorkflowInstances(status, definitionId, from, to, skip, take + 1)).ToList();
#pragma warning restore CS0612, CS0618
        }
        catch (NotImplementedException)
        {
            return Error(StatusCodes.Status501NotImplemented, "listing-not-supported",
                $"{store.GetType().Name} cannot list workflow instances. Open an instance by ID instead.");
        }

        var items = page
            .Take(take)
            .Select(wf => DtoMapper.ToSummary(wf, registry.GetDefinition(wf.WorkflowDefinitionId, wf.Version)))
            .ToList();
        return Ok(new Page<InstanceSummary>(items, skip, take, page.Count > take));
    }

    private static async Task<IResult> GetInstance(string id, IPersistenceProvider store, IWorkflowRegistry registry)
    {
        WorkflowInstance? wf;
        try
        {
            wf = await store.GetWorkflowInstance(id);
        }
        catch (InvalidOperationException)
        {
            // Some providers (e.g. the in-memory one) throw instead of returning null.
            wf = null;
        }

        return wf is null
            ? Error(StatusCodes.Status404NotFound, "instance-not-found", $"Workflow instance '{id}' was not found.")
            : Ok(DtoMapper.ToDetail(wf, registry.GetDefinition(wf.WorkflowDefinitionId, wf.Version)));
    }

    private static IResult GetActivity(HttpRequest request, ActivityFeed feed)
    {
        var take = Math.Clamp(ParseInt(request.Query["take"]) ?? 100, 1, 1000);
        return Ok(feed.Get(NullIfEmpty(request.Query["instanceId"]), take));
    }

    private static IResult GetActivityTotals(ActivityFeed feed) => Ok(feed.Totals());

    private static async Task<IResult> StartWorkflow(HttpRequest request, IWorkflowController controller, IWorkflowRegistry registry)
    {
        var body = await ReadBody<StartWorkflowRequest>(request);
        if (body is null || string.IsNullOrWhiteSpace(body.DefinitionId))
            return Error(StatusCodes.Status400BadRequest, "invalid-request", "definitionId is required.");

        var def = registry.GetDefinition(body.DefinitionId, body.Version);
        if (def is null)
            return Error(StatusCodes.Status404NotFound, "definition-not-found", $"Workflow '{body.DefinitionId}' is not registered.");

        object? data;
        try
        {
            data = DtoMapper.ToWorkflowData(body.Data, def.DataType);
        }
        catch (Exception ex)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid-data", $"Data does not match {def.DataType?.Name}: {ex.Message}");
        }

        try
        {
            var id = await controller.StartWorkflow(def.Id, def.Version, data, NullIfEmpty(body.Reference));
            return Ok(new StartWorkflowResponse(id));
        }
        catch (WorkflowNotRegisteredException ex)
        {
            return Error(StatusCodes.Status404NotFound, "definition-not-found", ex.Message);
        }
    }

    private static async Task<IResult> PublishEvent(HttpRequest request, IWorkflowController controller)
    {
        var body = await ReadBody<PublishEventRequest>(request);
        if (body is null || string.IsNullOrWhiteSpace(body.EventName))
            return Error(StatusCodes.Status400BadRequest, "invalid-request", "eventName is required.");

        await controller.PublishEvent(body.EventName, body.EventKey ?? string.Empty, DtoMapper.ToEventData(body.EventData),
            body.EffectiveDate?.ToUniversalTime());
        return Ok(new ActionResponse(true));
    }

    private static async Task<IResult> Act(Func<Task<bool>> action) => Ok(new ActionResponse(await action()));

    /// <summary>Rejects state-changing requests when the dashboard is configured read-only.</summary>
    internal static ValueTask<object?> RequireActions(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method))
            return next(context);

        var options = http.RequestServices.GetRequiredService<IOptions<DashboardOptions>>().Value;
        return options.AllowActions
            ? next(context)
            : ValueTask.FromResult<object?>(Error(StatusCodes.Status403Forbidden, "read-only", "The dashboard is read-only."));
    }

    private static async Task<T?> ReadBody<T>(HttpRequest request)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, Json, request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool TryParseStatus(string? value, out WorkflowStatus? status)
    {
        status = null;
        if (string.IsNullOrEmpty(value))
            return true;
        if (!Enum.TryParse<WorkflowStatus>(value, ignoreCase: true, out var parsed))
            return false;
        status = parsed;
        return true;
    }

    private static int? ParseInt(string? value) => int.TryParse(value, out var n) ? n : null;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IResult Ok<T>(T value) => Results.Json(value, Json);

    internal static IResult Error(int status, string code, string message) =>
        Results.Json(new ApiError(code, message), Json, statusCode: status);
}
