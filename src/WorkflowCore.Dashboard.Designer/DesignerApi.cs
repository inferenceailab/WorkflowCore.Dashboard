using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace WorkflowCore.Dashboard.Designer;

/// <summary>Endpoints under <c>{prefix}/api/designer</c>.</summary>
internal sealed class DesignerApi : IDashboardExtension
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Feature => "designer";

    public void MapEndpoints(RouteGroupBuilder api)
    {
        var designer = api.MapGroup("/designer");

        designer.MapGet("/catalog", (StepCatalog catalog) => Ok(catalog.Get()));
        designer.MapGet("/definitions", async (DesignerService service, CancellationToken ct) => Ok(await service.List(ct)));
        designer.MapGet("/definitions/{id}", async (string id, DesignerService service, CancellationToken ct) =>
            await service.Get(id, ct) is { } document
                ? Ok(document)
                : Error(StatusCodes.Status404NotFound, "not-found", $"No designer workflow '{id}'."));

        designer.MapPut("/definitions/{id}/draft", (string id, HttpRequest request, DesignerService service) =>
            Handle<SaveDesignRequest>(request, async body => Ok(await service.SaveDraft(id, body, request.HttpContext.RequestAborted))));

        designer.MapDelete("/definitions/{id}/draft", async (string id, DesignerService service, CancellationToken ct) =>
        {
            await service.DiscardDraft(id, ct);
            return Results.NoContent();
        });

        designer.MapPost("/validate", (HttpRequest request, DesignerService service) =>
            Handle<ValidateRequest>(request, body => Task.FromResult(Ok(service.Validate(body.Source)))));

        designer.MapPost("/definitions/{id}/publish", (string id, HttpRequest request, DesignerService service) =>
            Handle<SaveDesignRequest>(request, async body =>
            {
                var result = await service.Publish(id, body, request.HttpContext.RequestAborted);
                return result.Validation.Valid ? Ok(result) : Results.Json(result, Json, statusCode: StatusCodes.Status422UnprocessableEntity);
            }));

        designer.MapPost("/import", (HttpRequest request) =>
            Handle<ImportRequest>(request, body => Task.FromResult(Ok(DesignerService.Import(body.Text)))));
    }

    private static async Task<IResult> Handle<T>(HttpRequest request, Func<T, Task<IResult>> handler)
    {
        T? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<T>(request.Body, Json, request.HttpContext.RequestAborted);
        }
        catch (JsonException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid-request", ex.Message);
        }
        if (body is null)
            return Error(StatusCodes.Status400BadRequest, "invalid-request", "A request body is required.");

        try
        {
            return await handler(body);
        }
        catch (DesignerException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Code, ex.Message);
        }
    }

    private static IResult Ok<T>(T value) => Results.Json(value, Json);

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { code, message }, Json, statusCode: status);
}
