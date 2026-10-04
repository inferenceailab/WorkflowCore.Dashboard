using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace WorkflowCore.Dashboard.Ui;

/// <summary>Explains to a signed-in user why they cannot open the dashboard, and lets them switch accounts.</summary>
internal static class AccessDeniedPage
{
    public static bool IsPageRequest(HttpContext http) =>
        HttpMethods.IsGet(http.Request.Method)
        && !http.Request.Path.Value!.Contains("/api/", StringComparison.OrdinalIgnoreCase)
        && http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    /// <summary>Relative sign-out paths are relative to the dashboard root, wherever this page was requested.</summary>
    private static string Resolve(HttpContext http, string path)
    {
        if (path.StartsWith('/') || path.Contains("://"))
            return path;
        var prefix = http.RequestServices.GetService<DashboardRoute>()?.Prefix ?? "";
        return $"{http.Request.PathBase}{prefix}/{path}";
    }

    public static IResult Render(HttpContext http, DashboardOptions options)
    {
        var html = HtmlEncoder.Default;
        var user = http.User;
        var name = user.Identity?.Name ?? user.FindFirst("email")?.Value ?? user.FindFirst("sub")?.Value ?? "you";
        var groups = user.FindAll(DashboardClaims.Group).Select(c => c.Value).ToList();

        var groupsText = groups.Count == 0
            ? "<p>The identity provider sent no groups for this account.</p>"
            : $"<p>Groups received from the identity provider:</p><ul>{string.Concat(groups.Select(g => $"<li><code>{html.Encode(g)}</code></li>"))}</ul>";
        var signOut = options.SignOutPath is { } path
            ? $"<p><a href=\"{html.Encode(Resolve(http, path))}\">Sign in with another account</a></p>"
            : "";

        var page = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Access denied · {{html.Encode(options.Title)}}</title>
            <style>
              body { font: 15px/1.5 system-ui, "Segoe UI", sans-serif; margin: 0; padding: 48px 16px; color: #1d1b20; background: #f7f2fa; }
              main { max-width: 560px; margin: 0 auto; padding: 28px; border-radius: 12px; background: #fff; border: 1px solid #cac4d0; }
              h1 { margin: 0 0 12px; font-size: 22px; }
              code { font-family: ui-monospace, Consolas, monospace; }
              a { color: #6750a4; }
              @media (prefers-color-scheme: dark) { body { background: #141218; color: #e6e0e9; } main { background: #1d1b20; border-color: #49454f; } a { color: #d0bcff; } }
            </style></head>
            <body><main>
              <h1>You don't have access to {{html.Encode(options.Title)}}</h1>
              <p>You are signed in as <strong>{{html.Encode(name)}}</strong>, but this account has no dashboard role.
              Ask an administrator to add you to one of the dashboard's admin or viewer groups.</p>
              {{groupsText}}
              {{signOut}}
            </main></body></html>
            """;
        return Results.Content(page, "text/html; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
    }
}
