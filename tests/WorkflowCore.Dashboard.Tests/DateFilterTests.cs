using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WorkflowCore.Dashboard.Tests;

public sealed class DateFilterTests : IAsyncLifetime
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
                    services.AddWorkflowCoreDashboard();
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

    private async Task AssertInvalidDate(string path, string name)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid-date", body.GetProperty("code").GetString());
        Assert.Equal($"{name} 'not-a-date' is not a date.", body.GetProperty("message").GetString());
    }

    [Fact]
    public Task Instances_createdFrom_that_is_not_a_date_is_rejected() =>
        AssertInvalidDate("/workflows/api/instances?createdFrom=not-a-date", "createdFrom");

    [Fact]
    public Task Instances_createdTo_that_is_not_a_date_is_rejected() =>
        AssertInvalidDate("/workflows/api/instances?createdTo=not-a-date", "createdTo");

    [Fact]
    public Task Activity_before_that_is_not_a_date_is_rejected() =>
        AssertInvalidDate("/workflows/api/activity?before=not-a-date", "before");

    [Fact]
    public async Task Valid_ISO_dates_and_absent_dates_are_not_rejected()
    {
        foreach (var path in new[]
        {
            "/workflows/api/instances?createdFrom=2026-01-01T00:00:00Z&createdTo=2026-12-31",
            "/workflows/api/instances",
            "/workflows/api/activity?before=2026-01-01T00:00:00Z",
            "/workflows/api/activity",
        })
        {
            var response = await _client.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
