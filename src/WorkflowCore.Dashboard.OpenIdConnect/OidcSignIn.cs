using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WorkflowCore.Dashboard.OpenIdConnect;

/// <summary>
/// Configures the dashboard's own cookie and OpenID Connect schemes. They are named schemes, not defaults,
/// so the host app's sign-in (if any) is untouched, and the session cookie is only sent to the dashboard path.
/// </summary>
internal static class OidcSignIn
{
    public const string CookieScheme = "WorkflowCoreDashboard";
    public const string OidcScheme = "WorkflowCoreDashboard.OpenIdConnect";
    public const string Policy = "WorkflowCoreDashboard";

    private const string DefaultPrefix = "/workflows";

    public static void Register(IServiceCollection services)
    {
        services.AddAuthentication()
            .AddCookie(CookieScheme)
            .AddOpenIdConnect(OidcScheme, _ => { });

        services.AddOptions<CookieAuthenticationOptions>(CookieScheme)
            .Configure<DashboardOidcOptions, DashboardRoute>(ConfigureCookie);
        services.AddOptions<OpenIdConnectOptions>(OidcScheme)
            .Configure<DashboardOidcOptions, DashboardRoute>(ConfigureOpenIdConnect);

        services.AddAuthorization(o => o.AddPolicy(Policy, p => p.AddAuthenticationSchemes(CookieScheme).RequireAuthenticatedUser()));

        services.Configure<DashboardOptions>(o =>
        {
            o.AuthorizationPolicy = Policy;
            o.Authorization = DashboardRoles.HasAccess;
            o.ActionAuthorization = DashboardRoles.IsAdmin;
            o.SignOutPath = "signout";
        });
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie, DashboardOidcOptions oidc, DashboardRoute route)
    {
        cookie.Cookie = new DashboardCookieBuilder(route)
        {
            Name = ".WorkflowCoreDashboard",
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            SecurePolicy = CookieSecurePolicy.SameAsRequest,
        };
        cookie.ExpireTimeSpan = oidc.SessionLifetime;
        cookie.SlidingExpiration = true;
        // Signed-out users are sent to the identity provider.
        cookie.ForwardChallenge = OidcScheme;
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions o, DashboardOidcOptions oidc, DashboardRoute route)
    {
        var prefix = route.Prefix ?? DefaultPrefix;

        o.Authority = oidc.Authority;
        o.ClientId = oidc.ClientId;
        o.ClientSecret = oidc.ClientSecret;
        o.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
        o.ResponseType = "code";
        o.UsePkce = true;
        // Query responses are top-level GET navigations, so SameSite=Lax correlation cookies come back, even on HTTP in development.
        o.ResponseMode = "query";
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.NonceCookie.SameSite = SameSiteMode.Lax;
        o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        o.SignInScheme = CookieScheme;
        o.SignOutScheme = CookieScheme;
        o.CallbackPath = prefix + "/signin-oidc";
        o.SignedOutCallbackPath = prefix + "/signout-callback-oidc";
        // Where the provider's sign-out ends up when it returns without the original state.
        o.SignedOutRedirectUri = prefix + "/signed-out";

        o.Scope.Clear();
        foreach (var scope in oidc.Scopes.Distinct())
            o.Scope.Add(scope);

        // Keep the provider's claim names (groups, email, roles…) instead of mapping them to long URIs.
        o.MapInboundClaims = false;
        o.GetClaimsFromUserInfoEndpoint = true;
        o.SaveTokens = true;
        o.TokenValidationParameters.NameClaimType = "name";
        o.TokenValidationParameters.RoleClaimType = DashboardClaims.Role;
        foreach (var claim in new[] { "name", "preferred_username", "email", "email_verified", oidc.EmailClaim })
            o.ClaimActions.MapUniqueJsonKey(claim, claim);
        if (oidc.GroupsClaim is { } groups)
        {
            var root = groups.Split('.')[0];
            o.ClaimActions.MapJsonKey(groups, groups);
            if (root != groups)
                o.ClaimActions.MapJsonKey(root, root);
        }

        o.Events.OnRedirectToIdentityProvider = context =>
        {
            // API and hub calls from an expired session get 401; the UI reloads, which signs in again.
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith(prefix + "/api", StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/hub", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HandleResponse();
            }
            return Task.CompletedTask;
        };

        o.Events.OnTicketReceived = context =>
        {
            var (role, received) = DashboardRoles.Resolve(context.Principal!, oidc);
            context.Principal = DashboardPrincipal(context.Principal!, role, received);

            // Only the ID token is kept: it is the id_token_hint most providers need for signing out.
            var idToken = context.Properties!.GetTokenValue("id_token");
            context.Properties!.StoreTokens(idToken is null ? [] : [new AuthenticationToken { Name = "id_token", Value = idToken }]);

            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WorkflowCore.Dashboard.OpenIdConnect");
            if (role == DashboardRole.None)
            {
                // The usual setup mistake: the groups claim is missing or named differently. Say what arrived.
                logger.LogWarning(
                    "{User} signed in to the dashboard but has no role. {Claim} claim received: [{Groups}]",
                    context.Principal.Identity?.Name, oidc.GroupsClaim ?? "(no groups claim configured)", string.Join(", ", received));
            }
            else
            {
                logger.LogInformation("{User} signed in to the dashboard as {Role}", context.Principal.Identity?.Name, role);
            }
            return Task.CompletedTask;
        };

        o.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{prefix}/signed-out";
            if (oidc.SignOutUrl is { } signOutUrl)
            {
                context.Response.Redirect(signOutUrl(returnUrl));
                context.HandleResponse();
            }
            else if (string.IsNullOrEmpty(context.ProtocolMessage.IssuerAddress))
            {
                // The provider has no logout endpoint (Google): the dashboard session is closed, that is all we can do.
                context.Response.Redirect(returnUrl);
                context.HandleResponse();
            }
            return Task.CompletedTask;
        };

        o.Events.OnRemoteFailure = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/html; charset=utf-8";
            var retry = $"{context.Request.PathBase}{prefix}/";
            await context.Response.WriteAsync(SimplePage(
                "Sign-in failed",
                $"<p>Signing in with {Encode(oidc.ProviderName)} did not complete:</p><p><code>{Encode(context.Failure?.Message ?? "unknown error")}</code></p>" +
                $"<p><a href=\"{Encode(retry)}\">Try again</a></p>"));
        };

        oidc.ConfigureOpenIdConnect?.Invoke(o);
    }

    /// <summary>
    /// A small identity for the session cookie: who the user is and their dashboard role. Groups are only kept
    /// (up to 20) when they gave no role, to explain the "access denied" page.
    /// </summary>
    private static ClaimsPrincipal DashboardPrincipal(ClaimsPrincipal source, DashboardRole role, IReadOnlyList<string> groups)
    {
        var claims = new List<Claim>();
        foreach (var type in new[] { "sub", "email", "preferred_username" })
        {
            if (source.FindFirst(type) is { } claim)
                claims.Add(new Claim(type, claim.Value));
        }

        var name = source.FindFirst("name")?.Value ?? source.FindFirst("preferred_username")?.Value
            ?? source.FindFirst("email")?.Value ?? source.FindFirst("sub")?.Value ?? "unknown";
        claims.Add(new Claim("name", name));

        if (role == DashboardRole.None)
            claims.AddRange(groups.Take(20).Select(g => new Claim(DashboardClaims.Group, g)));
        else
            claims.Add(new Claim(DashboardClaims.Role, role == DashboardRole.Admin ? DashboardClaims.Admin : DashboardClaims.Viewer));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, OidcScheme, "name", DashboardClaims.Role));
    }

    internal static string SimplePage(string title, string body) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{Encode(title)}}</title>
        <style>
          body { font: 15px/1.5 system-ui, "Segoe UI", sans-serif; margin: 0; padding: 48px 16px; color: #1d1b20; background: #f7f2fa; }
          main { max-width: 560px; margin: 0 auto; padding: 28px; border-radius: 12px; background: #fff; border: 1px solid #cac4d0; }
          h1 { margin: 0 0 12px; font-size: 22px; }
          code { font-family: ui-monospace, Consolas, monospace; overflow-wrap: anywhere; }
          a { color: #6750a4; }
          @media (prefers-color-scheme: dark) { body { background: #141218; color: #e6e0e9; } main { background: #1d1b20; border-color: #49454f; } a { color: #d0bcff; } }
        </style></head>
        <body><main><h1>{{Encode(title)}}</h1>{{body}}</main></body></html>
        """;

    internal static string Encode(string value) => HtmlEncoder.Default.Encode(value);

    /// <summary>Scopes the session cookie to the dashboard path, including any PathBase of the host app.</summary>
    private sealed class DashboardCookieBuilder(DashboardRoute route) : RequestPathBaseCookieBuilder
    {
        public override CookieOptions Build(HttpContext context, DateTimeOffset expiresFrom)
        {
            var options = base.Build(context, expiresFrom);
            options.Path = $"{context.Request.PathBase}{route.Prefix ?? DefaultPrefix}";
            return options;
        }
    }
}
