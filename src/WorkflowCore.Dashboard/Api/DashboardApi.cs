using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.Journal;
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

    private static readonly DateTime StartedAt = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private static IResult GetConfig(IOptions<DashboardOptions> options, IPersistenceProvider store, IDashboardJournal journal)
    {
        var o = options.Value;
        var retention = journal.IsPersistent && o.JournalRetention > TimeSpan.Zero ? (int?)Math.Ceiling(o.JournalRetention.TotalDays) : null;
        return Ok(new DashboardConfig(
            o.Title,
            o.AllowActions,
            store.GetType().Name,
            StartedAt,
            new JournalInfo(journal.Name, journal.IsPersistent, retention, o.JournalStepEvents)));
    }

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

    private static async Task<IResult> GetInstances(HttpRequest request, InstanceListing listing, IOptions<DashboardOptions> options)
    {
        var q = request.Query;
        if (!TryParseStatus(q["status"], out var status))
            return Error(StatusCodes.Status400BadRequest, "invalid-status", $"Unknown status '{q["status"]}'.");

        var query = new InstanceListQuery(
            status,
            NullIfEmpty(q["definitionId"]),
            ParseDate(q["createdFrom"]),
            ParseDate(q["createdTo"]),
            Math.Max(0, ParseInt(q["skip"]) ?? 0),
            Math.Clamp(ParseInt(q["take"]) ?? 25, 1, options.Value.MaxPageSize));

        var page = await listing.List(query, request.HttpContext.RequestAborted);
        return page is null
            ? Error(StatusCodes.Status501NotImplemented, "listing-not-supported",
                "The persistence provider cannot list workflow instances. Open an instance by ID, or configure a persistent journal.")
            : Ok(page);
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

    private static async Task<IResult> GetActivity(HttpRequest request, IDashboardJournal journal)
    {
        var q = request.Query;
        var query = new ActivityQuery(
            NullIfEmpty(q["instanceId"]),
            ParseDate(q["before"]),
            Math.Clamp(ParseInt(q["take"]) ?? 100, 1, 1000),
            !string.Equals(q["steps"], "false", StringComparison.OrdinalIgnoreCase));
        return Ok(await journal.GetActivityAsync(query, request.HttpContext.RequestAborted));
    }

    /// <summary>Counts by event type over the last <c>hours</c> (default 24).</summary>
    private static async Task<IResult> GetActivityTotals(HttpRequest request, IDashboardJournal journal)
    {
        var hours = Math.Clamp(ParseInt(request.Query["hours"]) ?? 24, 1, 24 * 366);
        return Ok(await journal.GetTotalsAsync(DateTime.UtcNow.AddHours(-hours), request.HttpContext.RequestAborted));
    }

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
