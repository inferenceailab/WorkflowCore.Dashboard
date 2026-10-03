using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using WorkflowCore.Dashboard.Api;

namespace WorkflowCore.Dashboard.Ui;

/// <summary>Serves the Angular build that is embedded in this assembly, with SPA fallback to index.html.</summary>
internal static class EmbeddedUi
{
    private const string BaseHrefPlaceholder = "<base href=\"/\">";

    private static readonly Lazy<IFileProvider?> Files = new(() =>
    {
        try
        {
            return new ManifestEmbeddedFileProvider(typeof(EmbeddedUi).Assembly, "wwwroot");
        }
        catch (InvalidOperationException)
        {
            return null; // assembly built without the UI
        }
    });

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static void Map(RouteGroupBuilder group, string prefix)
    {
        group.MapGet("/", (HttpContext ctx) => ServeIndex(ctx, prefix));
        group.MapGet("/{**path}", (HttpContext ctx, string? path) => Serve(ctx, prefix, path ?? string.Empty));
    }

    private static IResult Serve(HttpContext ctx, string prefix, string path)
    {
        if (path.Equals("api", StringComparison.OrdinalIgnoreCase) || path.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            return DashboardApi.Error(StatusCodes.Status404NotFound, "not-found", "Unknown dashboard API route.");

        var file = Files.Value?.GetFileInfo(path);
        if (file is { Exists: true, IsDirectory: false } && !path.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            if (!ContentTypes.TryGetContentType(path, out var contentType))
                contentType = "application/octet-stream";

            // Angular emits content-hashed file names, so everything except index.html can be cached for good.
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Stream(file.CreateReadStream(), contentType);
        }

        // Unknown paths with an extension are missing assets; everything else is a client-side route.
        return Path.HasExtension(path) ? Results.NotFound() : ServeIndex(ctx, prefix);
    }

    private static IResult ServeIndex(HttpContext ctx, string prefix)
    {
        var file = Files.Value?.GetFileInfo("index.html");
        if (file is not { Exists: true })
        {
            return Results.Content(
                "<h1>Workflow Core Dashboard</h1><p>The UI was not built. Run <code>npm run build</code> in " +
                "src/WorkflowCore.Dashboard.UI, then rebuild WorkflowCore.Dashboard.</p>",
                "text/html");
        }

        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        var html = reader.ReadToEnd();

        // The UI is built with base href "/". Rewrite it so routing and asset URLs work under any prefix.
        var baseHref = $"{ctx.Request.PathBase}{prefix}/";
        html = html.Replace(BaseHrefPlaceholder, $"<base href=\"{baseHref}\">");

        ctx.Response.Headers.CacheControl = "no-cache, no-store";
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
