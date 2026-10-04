using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WorkflowCore.Dashboard.Tests;

/// <summary>Signs in whoever is named in the X-Test-User header: "name" or "name:role1,role2".</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var value))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = value.ToString().Split(':');
        var claims = new List<Claim> { new(ClaimTypes.Name, parts[0]) };
        if (parts.Length > 1)
            claims.AddRange(parts[1].Split(',').Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
    }
}

/// <summary>The setups recommended in docs/Security.md, checked end to end.</summary>
public sealed class AuthorizationTests
{
    private static async Task<IHost> Host(Action<DashboardOptions> dashboard, Action<RouteGroupBuilder>? group = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging().AddRouting();
                    services.AddAuthentication(TestAuthHandler.Name)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Name, null);
                    services.AddAuthorization(o => o.AddPolicy("WorkflowAdmins", p => p.RequireRole("workflow-admin")));
                    services.AddWorkflow();
                    services.AddWorkflowCoreDashboard(dashboard);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        var dashboard = e.MapWorkflowCoreDashboard("/workflows");
                        group?.Invoke(dashboard);
                    });
                }))
            .StartAsync();
    }

    private static HttpRequestMessage Get(string path, string? user) => As(new HttpRequestMessage(HttpMethod.Get, path), user);

    private static HttpRequestMessage PublishEvent(string? user) => As(
        new HttpRequestMessage(HttpMethod.Post, "/workflows/api/events")
        {
            Headers = { { "X-Wfc-Dashboard", "1" } },
            Content = JsonContent.Create(new { eventName = "Ping", eventKey = "1" }),
        },
        user);

    private static HttpRequestMessage As(HttpRequestMessage request, string? user)
    {
        if (user is not null)
            request.Headers.Add("X-Test-User", user);
        return request;
    }

    [Theory]
    [InlineData("/workflows/")]                   // the UI
    [InlineData("/workflows/instances/abc")]      // a client-side route
    [InlineData("/workflows/api/config")]         // the API
    public async Task A_policy_on_the_group_covers_ui_and_api(string path)
    {
        using var host = await Host(o => o.Authorization = _ => true, g => g.RequireAuthorization("WorkflowAdmins"));
        var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get(path, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Get(path, "sam"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get(path, "ada:workflow-admin"))).StatusCode);
    }

    [Fact]
    public async Task A_policy_on_the_group_covers_the_live_updates_hub()
    {
        using var host = await Host(o => o.Authorization = _ => true, g => g.RequireAuthorization("WorkflowAdmins"));
        var client = host.GetTestClient();
        HttpRequestMessage Negotiate(string? user) => As(new HttpRequestMessage(HttpMethod.Post, "/workflows/hub/negotiate?negotiateVersion=1"), user);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Negotiate(null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Negotiate("ada:workflow-admin"))).StatusCode);
    }

    [Fact]
    public async Task Viewers_can_read_while_only_admins_can_change()
    {
        // RequireAuthorization() sends signed-out users to the login; the function decides per role.
        using var host = await Host(
            o => o.Authorization = http =>
                http.User.IsInRole("workflow-admin")
                || (http.User.IsInRole("workflow-viewer") && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))),
            g => g.RequireAuthorization());
        var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Get("/workflows/", null))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/workflows/api/config", "val:workflow-viewer"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(PublishEvent("val:workflow-viewer"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(PublishEvent("ada:workflow-admin"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Get("/workflows/api/config", "sam"))).StatusCode);
    }
}
