using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkflowCore.Interface;

namespace WorkflowCore.Dashboard.Tests;

public sealed class StartWorkflowVersionTests
{
    public class Noop : WorkflowCore.Models.StepBody
    {
        public override WorkflowCore.Models.ExecutionResult Run(WorkflowCore.Interface.IStepExecutionContext context) => WorkflowCore.Models.ExecutionResult.Next();
    }

    public class HelloV2 : IWorkflow
    {
        public string Id => "HelloWorld";
        public int Version => 2;
        public void Build(IWorkflowBuilder<object> builder) => builder.StartWith<Noop>();
    }

    public class HelloV1 : IWorkflow
    {
        public string Id => "HelloWorld";
        public int Version => 1;
        public void Build(IWorkflowBuilder<object> builder) => builder.StartWith<Noop>();
    }

    private static async Task<IHost> Start()
    {
        return await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging().AddRouting();
                    services.AddWorkflow();
                    services.AddWorkflowCoreDashboard(_ => { });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapWorkflowCoreDashboard("/workflows"));
                }))
            .StartAsync();
    }

    private static async Task<HttpResponseMessage> Start(IHost host, string json)
    {
        var registry = host.Services.GetRequiredService<IWorkflowRegistry>();
        // Registered out of order to prove the listing is sorted.
        registry.RegisterWorkflow(new HelloV2());
        registry.RegisterWorkflow(new HelloV1());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/api/instances")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Wfc-Dashboard", "1");
        return await host.GetTestClient().SendAsync(request);
    }

    private static async Task AssertNotFound(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("definition-not-found", body.GetProperty("code").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unregistered_version_names_the_version_and_lists_registered_versions_ascending()
    {
        using var host = await Start();
        await AssertNotFound(await Start(host, """{"definitionId":"HelloWorld","version":3}"""),
            "Workflow 'HelloWorld' version 3 is not registered. Registered versions: 1, 2.");
        await host.StopAsync();
    }

    [Fact]
    public async Task Unknown_definition_id_keeps_the_plain_not_registered_message()
    {
        using var host = await Start();
        await AssertNotFound(await Start(host, """{"definitionId":"X","version":1}"""),
            "Workflow 'X' is not registered.");
        await host.StopAsync();
    }

    [Fact]
    public async Task Missing_version_still_starts_the_latest_version()
    {
        using var host = await Start();
        var response = await Start(host, """{"definitionId":"HelloWorld"}""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await host.StopAsync();
    }
}
