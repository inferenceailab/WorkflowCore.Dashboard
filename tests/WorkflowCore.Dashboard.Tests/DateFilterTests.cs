using System.Net;
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

    private async Task<(HttpStatusCode Status, string? Code, string? Message)> Get(string path)
    {
        var response = await _client.GetAsync("/workflows/api" + path);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.BadRequest)
            return (response.StatusCode, null, null);
        using var doc = JsonDocument.Parse(body);
        return (response.StatusCode, doc.RootElement.GetProperty("code").GetString(), doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Instances_with_an_unparsable_createdFrom_return_invalid_date()
    {
        var (status, code, message) = await Get("/instances?createdFrom=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-date", code);
        Assert.Equal("createdFrom must be an ISO 8601 date, got 'not-a-date'.", message);
    }

    [Fact]
    public async Task Instances_with_an_unparsable_createdTo_return_invalid_date_naming_createdTo()
    {
        var (status, code, message) = await Get("/instances?createdTo=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-date", code);
        Assert.StartsWith("createdTo ", message);
    }

    [Fact]
    public async Task Activity_with_an_unparsable_before_returns_invalid_date_naming_before()
    {
        var (status, code, message) = await Get("/activity?before=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-date", code);
        Assert.StartsWith("before ", message);
    }

    [Fact]
    public async Task Instances_with_createdFrom_after_createdTo_return_invalid_date_range()
    {
        var (status, code, _) = await Get("/instances?createdFrom=2026-10-09&createdTo=2026-10-01");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid-date-range", code);
    }

    [Theory]
    [InlineData("/instances?createdFrom=2026-10-01&createdTo=2026-10-09")]
    [InlineData("/instances?createdFrom=&createdTo=")]
    [InlineData("/instances")]
    [InlineData("/activity?before=2026-10-09T00:00:00Z")]
    [InlineData("/activity?before=")]
    [InlineData("/activity")]
    public async Task Valid_empty_and_missing_dates_are_not_rejected(string path)
    {
        var (status, _, _) = await Get(path);
        Assert.NotEqual(HttpStatusCode.BadRequest, status);
    }
}
