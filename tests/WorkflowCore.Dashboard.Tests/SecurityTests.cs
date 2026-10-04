using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkflowCore.Dashboard.Designer;

namespace WorkflowCore.Dashboard.Tests;

public sealed class SecurityTests : IAsyncLifetime
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
                    services.AddWorkflowCoreDashboard().AddDesigner();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapWorkflowCoreDashboard("/workflows"));
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

    private static HttpRequestMessage PublishEvent(bool withHeader, string? fetchSite = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/api/events")
        {
            Content = JsonContent.Create(new { eventName = "Ping", eventKey = "1" }),
        };
        if (withHeader)
            request.Headers.Add("X-Wfc-Dashboard", "1");
        if (fetchSite is not null)
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        return request;
    }

    [Fact]
    public async Task Changes_without_the_dashboard_header_are_rejected()
    {
        // What a cross-site form post looks like: no custom header.
        var response = await _client.SendAsync(PublishEvent(withHeader: false));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("forbidden-origin", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Changes_from_another_site_are_rejected_even_with_the_header()
    {
        var response = await _client.SendAsync(PublishEvent(withHeader: true, fetchSite: "cross-site"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Changes_from_the_dashboard_are_accepted()
    {
        var response = await _client.SendAsync(PublishEvent(withHeader: true, fetchSite: "same-origin"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reads_need_no_header()
    {
        var response = await _client.GetAsync("/workflows/api/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Responses_forbid_framing_and_sniffing()
    {
        var response = await _client.GetAsync("/workflows/");

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData("127.0.0.1", "localhost", true)]
    [InlineData("127.0.0.1", "127.0.0.1", true)]
    [InlineData("::1", "[::1]", true)]
    [InlineData("127.0.0.1", "attacker.example", false)] // DNS rebinding
    [InlineData("203.0.113.7", "localhost", false)]      // another machine
    public void Local_requests_are_recognised_by_address_and_host_name(string remote, string host, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Host = new HostString(host, 5290);

        Assert.Equal(expected, DashboardOptions.IsLocalRequest(context));
    }

    [Fact]
    public void Yaml_type_tags_are_refused()
    {
        var yaml = """
            Id: Evil
            Steps:
              - !System.Diagnostics.ProcessStartInfo
                FileName: calc.exe
            """;

        var ex = Assert.Throws<DesignerException>(() => DesignerService.Import(yaml));
        Assert.Contains("tags", ex.Message);
    }

    [Fact]
    public void Yaml_scalars_keep_workflow_core_semantics()
    {
        var source = DesignerService.Import("""
            Id: Plain
            Version: 3
            Steps:
              - Id: a
                StepType: WorkflowCore.Primitives.Delay, WorkflowCore
                NextStepId: ~
                ProceedOnCancel: true
                Inputs:
                  Period: TimeSpan.FromSeconds(5)
            """).Source;

        var step = source["Steps"]![0]!;
        Assert.Equal(3, source["Version"]!.GetValue<int>());
        Assert.Null(step["NextStepId"]);
        Assert.True(step["ProceedOnCancel"]!.GetValue<bool>());
        Assert.Equal("TimeSpan.FromSeconds(5)", step["Inputs"]!["Period"]!.GetValue<string>());
    }

    [Fact]
    public void Only_catalog_types_can_be_used()
    {
        var validator = _host.Services.GetRequiredService<DefinitionValidator>();
        var source = new System.Text.Json.Nodes.JsonObject
        {
            ["Id"] = "Sneaky",
            ["DataType"] = "System.Text.StringBuilder, System.Runtime",
            ["Steps"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["Id"] = "a",
                // A real step type, but not one the designer offers.
                ["StepType"] = "WorkflowCore.Primitives.OutcomeSwitch, WorkflowCore",
            }),
        };

        var result = validator.Validate(source);

        Assert.Contains(result.Issues, i => i.StepId is null && i.Message.Contains("StringBuilder"));
        Assert.Contains(result.Issues, i => i.StepId == "a" && i.Message.Contains("not offered"));
    }
}
