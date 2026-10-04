using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using WorkflowCore.Dashboard.OpenIdConnect;

namespace WorkflowCore.Dashboard.Tests;

public class DashboardRolesTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    private static DashboardOidcOptions Options(Action<DashboardOidcOptions> configure)
    {
        var options = new DashboardOidcOptions();
        configure(options);
        return options;
    }

    [Fact]
    public void Groups_as_one_claim_each_or_as_a_json_array()
    {
        Assert.Equal(["a", "b"], DashboardRoles.GroupsOf(User(("groups", "a"), ("groups", "b")), "groups"));
        Assert.Equal(["a", "b"], DashboardRoles.GroupsOf(User(("groups", "[\"a\",\"b\"]")), "groups"));
    }

    [Fact]
    public void Keycloak_roles_are_read_from_a_path_inside_a_json_claim()
    {
        var user = User(("realm_access", """{"roles":["offline_access","workflow-admin"]}"""));
        Assert.Equal(["offline_access", "workflow-admin"], DashboardRoles.GroupsOf(user, "realm_access.roles"));
        Assert.Empty(DashboardRoles.GroupsOf(user, "realm_access.missing"));
    }

    [Fact]
    public void Zitadel_roles_are_the_keys_of_a_json_object()
    {
        var user = User(("urn:zitadel:iam:org:project:roles", """{"workflow-admin":{"2716":"acme.zitadel.cloud"},"viewer":{"2716":"acme.zitadel.cloud"}}"""));
        Assert.Equal(["workflow-admin", "viewer"], DashboardRoles.GroupsOf(user, "urn:zitadel:iam:org:project:roles"));
    }

    [Fact]
    public void Auth0_namespaced_claims_with_dots_match_as_a_whole()
    {
        var user = User(("https://workflows.example.com/roles", "workflow-admin"));
        Assert.Equal(["workflow-admin"], DashboardRoles.GroupsOf(user, "https://workflows.example.com/roles"));
    }

    [Fact]
    public void Admin_wins_over_viewer_and_unmatched_users_get_the_default_role()
    {
        var options = Options(o =>
        {
            o.AdminGroups.Add("Workflow-Admins");
            o.ViewerGroups.Add("everyone");
        });

        Assert.Equal(DashboardRole.Admin, DashboardRoles.Resolve(User(("groups", "everyone"), ("groups", "workflow-admins")), options).Role);
        Assert.Equal(DashboardRole.Viewer, DashboardRoles.Resolve(User(("groups", "everyone")), options).Role);
        Assert.Equal(DashboardRole.None, DashboardRoles.Resolve(User(("groups", "other")), options).Role);

        options.DefaultRole = DashboardRole.Viewer;
        Assert.Equal(DashboardRole.Viewer, DashboardRoles.Resolve(User(("groups", "other")), options).Role);
    }

    [Fact]
    public void Unverified_email_addresses_grant_nothing()
    {
        var options = Options(o => o.AdminUsers.Add("ada@example.com"));

        Assert.Equal(DashboardRole.Admin, DashboardRoles.Resolve(User(("email", "ADA@example.com")), options).Role);
        Assert.Equal(DashboardRole.None, DashboardRoles.Resolve(User(("email", "ada@example.com"), ("email_verified", "false")), options).Role);
    }

    [Fact]
    public void Google_domains_need_the_workspace_claim_not_just_an_address()
    {
        var google = new GoogleOptions { DefaultRole = DashboardRole.Viewer };
        google.AllowedDomains.Add("example.com");

        Assert.Equal(DashboardRole.Viewer, DashboardRoles.Resolve(User(("email", "val@example.com"), ("hd", "example.com")), google).Role);
        // A personal Google account registered with a company address.
        Assert.Equal(DashboardRole.None, DashboardRoles.Resolve(User(("email", "val@example.com")), google).Role);
    }

    [Fact]
    public void Allowed_domains_limit_every_role()
    {
        var options = Options(o =>
        {
            o.DefaultRole = DashboardRole.Viewer;
            o.AllowedDomains.Add("example.com");
        });

        Assert.Equal(DashboardRole.Viewer, DashboardRoles.Resolve(User(("email", "val@example.com")), options).Role);
        Assert.Equal(DashboardRole.None, DashboardRoles.Resolve(User(("email", "val@example.com.evil.io")), options).Role);
        Assert.Equal(DashboardRole.None, DashboardRoles.Resolve(User(("sub", "no-email")), options).Role);
    }
}

public class ProviderPresetTests
{
    private static T Apply<T>(T options) where T : DashboardOidcOptions
    {
        options.ApplyProviderDefaults();
        return options;
    }

    [Fact]
    public void Each_provider_gets_its_authority_and_groups_claim()
    {
        var okta = Apply(new OktaOptions { Domain = "acme.okta.com" });
        Assert.Equal("https://acme.okta.com/oauth2/default", okta.Authority);
        Assert.Equal("groups", okta.GroupsClaim);
        Assert.DoesNotContain("groups", okta.Scopes);

        var oktaOrg = Apply(new OktaOptions { Domain = "acme.okta.com", AuthorizationServerId = null });
        Assert.Equal("https://acme.okta.com", oktaOrg.Authority);
        Assert.Contains("groups", oktaOrg.Scopes);

        var entra = Apply(new EntraIdOptions { TenantId = "contoso.onmicrosoft.com" });
        Assert.Equal("https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0", entra.Authority);
        Assert.Equal("roles", entra.GroupsClaim);

        Assert.Equal("https://acme.eu.auth0.com/", Apply(new Auth0Options { Domain = "acme.eu.auth0.com" }).Authority);
        Assert.Equal("https://sso.example.com/realms/ops", Apply(new KeycloakOptions { BaseUrl = "https://sso.example.com/", Realm = "ops" }).Authority);
        Assert.Equal("https://accounts.google.com", Apply(new GoogleOptions()).Authority);

        var cognito = Apply(new CognitoOptions { Region = "eu-west-1", UserPoolId = "eu-west-1_abc", ClientId = "client", Domain = "acme.auth.eu-west-1.amazoncognito.com" });
        Assert.Equal("https://cognito-idp.eu-west-1.amazonaws.com/eu-west-1_abc", cognito.Authority);
        Assert.Equal("cognito:groups", cognito.GroupsClaim);
        Assert.Equal(
            "https://acme.auth.eu-west-1.amazoncognito.com/logout?client_id=client&logout_uri=https%3A%2F%2Fapp%2Fworkflows%2Fsigned-out",
            cognito.SignOutUrl!("https://app/workflows/signed-out"));
    }

    [Fact]
    public void Settings_pick_the_provider_and_fill_its_options()
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SignIn:Provider"] = "Okta",
            ["SignIn:Domain"] = "acme.okta.com",
            ["SignIn:ClientId"] = "0oa123",
            ["SignIn:ClientSecret"] = "from-env",
            ["SignIn:AdminGroups:0"] = "Workflow Admins",
            ["SignIn:ViewerGroups:0"] = "Engineering",
            ["SignIn:SessionLifetime"] = "02:00:00",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddWorkflow();
        services.AddWorkflowCoreDashboard().UseSignIn(settings.GetSection("SignIn"));
        using var provider = services.BuildServiceProvider();

        var okta = Assert.IsType<OktaOptions>(provider.GetRequiredService<DashboardOidcOptions>());
        Assert.Equal("https://acme.okta.com/oauth2/default", okta.Authority);
        Assert.Equal(["Workflow Admins"], okta.AdminGroups);
        Assert.Equal(["Engineering"], okta.ViewerGroups);
        Assert.Equal(TimeSpan.FromHours(2), okta.SessionLifetime);
        Assert.Equal(["openid", "profile", "email"], okta.Scopes);

        var unknown = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["SignIn:Provider"] = "Okat" }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddWorkflowCoreDashboard().UseSignIn(unknown.GetSection("SignIn")));
        Assert.Contains("SignIn:Provider", error.Message);
    }

    [Fact]
    public void Mistakes_are_reported_in_plain_words()
    {
        var problems = Apply(new Auth0Options { Domain = "acme.auth0.com" }).Validate().ToList();
        Assert.Contains(problems, p => p.Contains("ClientId"));
        Assert.Contains(problems, p => p.Contains("Nobody could use the dashboard"));

        var auth0 = Apply(new Auth0Options { Domain = "acme.auth0.com", ClientId = "id" });
        auth0.AdminGroups.Add("admins");
        Assert.Contains(auth0.Validate(), p => p.Contains("GroupsClaim"));

        var http = new DashboardOidcOptions { Authority = "http://idp.local", ClientId = "id", DefaultRole = DashboardRole.Viewer };
        Assert.Contains(http.Validate(), p => p.Contains("HTTPS"));

        var anyTenant = Apply(new EntraIdOptions { TenantId = "common", ClientId = "id", DefaultRole = DashboardRole.Viewer });
        Assert.Contains(anyTenant.Validate(), p => p.Contains("other organizations"));
    }
}

/// <summary>The dashboard with sign-in, against a stand-in provider whose metadata is configured in place.</summary>
public sealed class OpenIdConnectSignInTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging().AddRouting();
                    services.AddWorkflow();
                    services.AddWorkflowCoreDashboard().UseOpenIdConnect(o =>
                    {
                        o.Authority = "http://idp.test";
                        o.RequireHttpsMetadata = false;
                        o.ClientId = "dashboard";
                        o.ClientSecret = "secret";
                        o.AdminGroups.Add("workflow-admins");
                        o.ViewerGroups.Add("workflow-viewers");
                        o.ConfigureOpenIdConnect = oidc => oidc.Configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = "http://idp.test",
                            AuthorizationEndpoint = "http://idp.test/authorize",
                            EndSessionEndpoint = "http://idp.test/logout",
                        };
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapWorkflowCoreDashboard("/workflows");
                        // Stands in for a completed provider sign-in: issues the dashboard's session cookie.
                        e.MapGet("/test-signin", (HttpContext http, string role) =>
                        {
                            var claims = new List<Claim> { new("sub", "1"), new("name", "Ada"), new("email", "ada@example.com") };
                            claims.Add(role is "none"
                                ? new Claim(DashboardClaims.Group, "marketing")
                                : new Claim(DashboardClaims.Role, role));
                            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", DashboardClaims.Role));
                            return Results.SignIn(principal, authenticationScheme: OidcSignIn.CookieScheme);
                        });
                    });
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<string> SessionCookie(string role)
    {
        var response = await _client.GetAsync($"/test-signin?role={role}");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".WorkflowCoreDashboard"));
        Assert.Contains("path=/workflows", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        return cookie.Split(';')[0];
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? cookie, bool html = false)
    {
        var request = new HttpRequestMessage(method, path);
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (html)
            request.Headers.Add("Accept", "text/html");
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-Wfc-Dashboard", "1");
            request.Content = JsonContent.Create(new { eventName = "Ping", eventKey = "1" });
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Signed_out_visitors_are_sent_to_the_provider_with_pkce()
    {
        var response = await Send(HttpMethod.Get, "/workflows/instances/abc", null, html: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("http://idp.test/authorize?", location);
        Assert.Contains("client_id=dashboard", location);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%2Fworkflows%2Fsignin-oidc", location);
        // The answer comes back as a query (the default for the code flow), not as a cross-site form post.
        Assert.DoesNotContain("response_mode=form_post", location);
        Assert.Contains("code_challenge_method=S256", location);
    }

    [Fact]
    public async Task Api_calls_without_a_session_get_401_instead_of_a_redirect()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/workflows/api/config", null)).StatusCode);
    }

    [Fact]
    public async Task Admins_can_act()
    {
        var cookie = await SessionCookie(DashboardClaims.Admin);

        var config = await (await Send(HttpMethod.Get, "/workflows/api/config", cookie)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(config.GetProperty("allowActions").GetBoolean());
        Assert.Equal("admin", config.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("signout", config.GetProperty("signOutPath").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/workflows/api/events", cookie)).StatusCode);
    }

    [Fact]
    public async Task Viewers_can_look_but_not_act()
    {
        var cookie = await SessionCookie(DashboardClaims.Viewer);

        var config = await (await Send(HttpMethod.Get, "/workflows/api/config", cookie)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(config.GetProperty("allowActions").GetBoolean());
        Assert.Equal("Ada", config.GetProperty("user").GetProperty("name").GetString());

        var change = await Send(HttpMethod.Post, "/workflows/api/events", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Contains("not-allowed", await change.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Users_without_a_role_see_why()
    {
        var cookie = await SessionCookie("none");

        var page = await Send(HttpMethod.Get, "/workflows/instances/abc", cookie, html: true);
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Ada", html);
        Assert.Contains("marketing", html);
        Assert.Contains("href=\"/workflows/signout\"", html);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/workflows/api/config", cookie)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_provider_session_too()
    {
        var response = await Send(HttpMethod.Get, "/workflows/signout", await SessionCookie(DashboardClaims.Admin));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://idp.test/logout?", response.Headers.Location!.ToString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".WorkflowCoreDashboard=;"));
    }

    [Fact]
    public async Task Other_websites_cannot_sign_people_out()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/workflows/signout");
        request.Headers.Add("Cookie", await SessionCookie(DashboardClaims.Admin));
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        Assert.Contains("Do you want to sign out", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sign_out_pages_work_without_a_session()
    {
        var signout = await Send(HttpMethod.Get, "/workflows/signout", null);
        Assert.Equal("/workflows/signed-out", signout.Headers.Location!.ToString());

        var page = await Send(HttpMethod.Get, "/workflows/signed-out", null);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("signed out", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Configuration_mistakes_stop_the_app_from_starting()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddWorkflow();
        services.AddWorkflowCoreDashboard().UseOkta(o => o.Domain = "acme.okta.com");
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<DashboardOidcOptions>());
        Assert.Contains("Okta: ClientId is required.", error.Failures);
    }
}
