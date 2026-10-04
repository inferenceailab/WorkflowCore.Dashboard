using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace WorkflowCore.Dashboard.OpenIdConnect;

/// <summary>
/// Sign-in to the dashboard with an identity provider. Pick the method for your provider; all of them use
/// OpenID Connect, and <see cref="UseOpenIdConnect"/> works with any other compliant provider.
/// </summary>
public static class OpenIdConnectDashboardExtensions
{
    /// <summary>
    /// Sign-in configured from settings, e.g. the <c>Dashboard:SignIn</c> section of appsettings.json. Its
    /// <c>Provider</c> picks the identity provider (Okta, EntraId, Auth0, Keycloak, Google, Cognito or OpenIdConnect);
    /// the other keys are that provider's options, so secrets can come from environment variables or a vault.
    /// </summary>
    public static DashboardBuilder UseSignIn(this DashboardBuilder builder, IConfiguration section)
    {
        var provider = section["Provider"];
        return provider?.Replace(" ", "").ToLowerInvariant() switch
        {
            "okta" => builder.UseOkta(o => section.Bind(o)),
            "entraid" or "azuread" or "entra" => builder.UseEntraId(o => section.Bind(o)),
            "auth0" => builder.UseAuth0(o => section.Bind(o)),
            "keycloak" => builder.UseKeycloak(o => section.Bind(o)),
            "google" => builder.UseGoogle(o => section.Bind(o)),
            "cognito" or "amazoncognito" => builder.UseCognito(o => section.Bind(o)),
            "openidconnect" or "oidc" => builder.UseOpenIdConnect(o => section.Bind(o)),
            _ => throw new InvalidOperationException(
                $"Unknown sign-in provider \"{provider}\" in {(section as IConfigurationSection)?.Path ?? "configuration"}:Provider. " +
                "Use Okta, EntraId, Auth0, Keycloak, Google, Cognito or OpenIdConnect."),
        };
    }

    /// <summary>Any OpenID Connect provider: Ping Identity, OneLogin, JumpCloud, Zitadel, Authentik, Duo…</summary>
    public static DashboardBuilder UseOpenIdConnect(this DashboardBuilder builder, Action<DashboardOidcOptions> configure) =>
        builder.UseIdentityProvider(configure);

    public static DashboardBuilder UseOkta(this DashboardBuilder builder, Action<OktaOptions> configure) =>
        builder.UseIdentityProvider(configure);

    public static DashboardBuilder UseEntraId(this DashboardBuilder builder, Action<EntraIdOptions> configure) =>
        builder.UseIdentityProvider(configure);

    public static DashboardBuilder UseAuth0(this DashboardBuilder builder, Action<Auth0Options> configure) =>
        builder.UseIdentityProvider(configure);

    public static DashboardBuilder UseKeycloak(this DashboardBuilder builder, Action<KeycloakOptions> configure) =>
        builder.UseIdentityProvider(configure);

    public static DashboardBuilder UseGoogle(this DashboardBuilder builder, Action<GoogleOptions> configure) =>
        builder.UseIdentityProvider(configure);

    public static DashboardBuilder UseCognito(this DashboardBuilder builder, Action<CognitoOptions> configure) =>
        builder.UseIdentityProvider(configure);

    private static DashboardBuilder UseIdentityProvider<TOptions>(this DashboardBuilder builder, Action<TOptions> configure)
        where TOptions : DashboardOidcOptions, new()
    {
        var services = builder.Services;
        if (services.Any(d => d.ServiceType == typeof(DashboardOidcOptions)))
            throw new InvalidOperationException("The dashboard can use one identity provider; another one is already configured.");

        services.AddSingleton<DashboardOidcOptions>(_ =>
        {
            var options = new TOptions();
            configure(options);
            options.ApplyProviderDefaults();
            var problems = options.Validate().ToList();
            if (problems.Count > 0)
                throw new OptionsValidationException(typeof(TOptions).Name, typeof(TOptions), problems.Select(p => $"{options.ProviderName}: {p}"));
            return options;
        });
        services.AddHostedService<ValidateOnStart>();
        services.AddSingleton<IDashboardExtension, SignInEndpoints>();
        OidcSignIn.Register(services);
        return builder;
    }

    /// <summary>Reports configuration mistakes when the app starts, not at the first sign-in.</summary>
    private sealed class ValidateOnStart(IServiceProvider services) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            services.GetRequiredService<DashboardOidcOptions>();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Sign-out and the page shown afterwards, under the dashboard prefix.</summary>
    private sealed class SignInEndpoints : IDashboardExtension
    {
        public string Feature => "sign-in";

        public void MapEndpoints(RouteGroupBuilder api)
        {
        }

        public void MapDashboardEndpoints(RouteGroupBuilder dashboard)
        {
            dashboard.MapGet("/signout", (HttpContext http, DashboardRoute route) =>
            {
                var signedOut = $"{http.Request.PathBase}{route.Prefix}/signed-out";
                // Another website linking here must not sign people out: confirm instead.
                if (http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                {
                    var self = $"{http.Request.PathBase}{route.Prefix}/signout";
                    var body = $"<p>Do you want to sign out of the dashboard?</p><p><a href=\"{OidcSignIn.Encode(self)}\">Sign out</a></p>";
                    return Results.Content(OidcSignIn.SimplePage("Sign out", body), "text/html; charset=utf-8");
                }
                // Without a session there is no ID token to end the provider's session with.
                return http.User.Identity?.IsAuthenticated == true
                    ? Results.SignOut(new AuthenticationProperties { RedirectUri = signedOut }, [OidcSignIn.CookieScheme, OidcSignIn.OidcScheme])
                    : Results.Redirect(signedOut);
            }).AllowAnonymous();

            dashboard.MapGet("/signed-out", (HttpContext http, DashboardRoute route, IOptions<DashboardOptions> options) =>
            {
                var home = $"{http.Request.PathBase}{route.Prefix}/";
                var body = $"<p>You are signed out of {OidcSignIn.Encode(options.Value.Title)}.</p><p><a href=\"{OidcSignIn.Encode(home)}\">Sign in again</a></p>";
                return Results.Content(OidcSignIn.SimplePage("Signed out", body), "text/html; charset=utf-8");
            }).AllowAnonymous();
        }
    }
}
