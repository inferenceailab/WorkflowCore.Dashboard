using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WorkflowCore.Dashboard.Tests;

public sealed class InvalidJsonTests
{
    private static async Task<(IHost Host, HttpClient Client)> Start()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging().AddRouting();
                    services.AddWorkflow();
                    services.AddWorkflowCoreDashboard();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapWorkflowCoreDashboard("/workflows"));
                }))
            .StartAsync();
        return (host, host.GetTestClient());
    }

    private static async Task<(HttpStatusCode Status, string Code, string Message)> Post(string path, string body)
    {
        var (host, client) = await Start();
        using (host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/workflows/api/{path}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Wfc-Dashboard", "1");
            var response = await client.SendAsync(request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (response.StatusCode, json.GetProperty("code").GetString()!, json.GetProperty("message").GetString()!);
        }
    }

    [Fact]
    public async Task Starting_an_instance_with_a_malformed_body_returns_400_invalid_json()
    {
        var (status, code, message) = await Post("instances", "{\"definitionId\": \"x\"");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-json", code);
        Assert.StartsWith("The request body is not valid JSON: ", message);
    }

    [Fact]
    public async Task Starting_an_instance_with_a_wrong_typed_field_names_its_json_path()
    {
        var (status, code, message) = await Post("instances", "{\"definitionId\": \"x\", \"version\": \"abc\"}");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-json", code);
        Assert.Contains("$.version", message);
    }

    [Fact]
    public async Task Publishing_an_event_with_a_malformed_body_returns_400_invalid_json()
    {
        var (status, code, message) = await Post("events", "{\"eventName\": \"x\"");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-json", code);
        Assert.StartsWith("The request body is not valid JSON: ", message);
    }

    [Theory]
    [InlineData("instances", "definitionId is required.")]
    [InlineData("events", "eventName is required.")]
    public async Task An_empty_body_returns_400_invalid_request(string path, string expected)
    {
        var (status, code, message) = await Post(path, "");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-request", code);
        Assert.Equal(expected, message);
    }

    [Theory]
    [InlineData("instances", "definitionId is required.")]
    [InlineData("events", "eventName is required.")]
    public async Task Valid_json_without_the_required_field_returns_400_invalid_request(string path, string expected)
    {
        var (status, code, message) = await Post(path, "{}");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-request", code);
        Assert.Equal(expected, message);
    }

    [Fact]
    public async Task A_valid_event_request_is_still_accepted()
    {
        var (host, client) = await Start();
        using (host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/api/events")
            {
                Content = new StringContent("{\"eventName\": \"e\", \"eventKey\": \"k\"}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Wfc-Dashboard", "1");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_valid_start_request_for_an_unknown_definition_still_returns_404()
    {
        var (status, code, _) = await Post("instances", "{\"definitionId\": \"nope\"}");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("definition-not-found", code);
    }
}
