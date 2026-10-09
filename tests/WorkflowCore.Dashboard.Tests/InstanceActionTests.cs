using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Tests;

public sealed class InstanceActionTests
{
    /// <summary>Wraps the real provider but returns null for every instance lookup, like providers that do not throw.</summary>
    public class NullLookupProxy : DispatchProxy
    {
        public IPersistenceProvider Inner { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IPersistenceProvider.GetWorkflowInstance) && targetMethod.GetParameters().Length == 2)
                return Task.FromResult<WorkflowInstance?>(null);
            if (targetMethod.Name == nameof(IPersistenceProvider.GetWorkflowInstance))
                return Task.FromResult<WorkflowInstance?>(null);
            return targetMethod.Invoke(Inner, args);
        }
    }

    private static async Task<(IHost Host, HttpClient Client)> Start(bool nullProvider, bool readOnly = false)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging().AddRouting();
                    services.AddWorkflow();
                    services.AddWorkflowCoreDashboard(o => o.AllowActions = !readOnly);
                    if (nullProvider)
                    {
                        var real = services.Single(d => d.ServiceType == typeof(IPersistenceProvider));
                        services.Remove(real);
                        services.AddSingleton(sp =>
                        {
                            var inner = real.ImplementationFactory is { } f
                                ? (IPersistenceProvider)f(sp)
                                : (IPersistenceProvider)ActivatorUtilities.CreateInstance(sp, real.ImplementationType ?? real.ImplementationInstance!.GetType());
                            var proxy = DispatchProxy.Create<IPersistenceProvider, NullLookupProxy>();
                            ((NullLookupProxy)proxy).Inner = inner;
                            return proxy;
                        });
                    }
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapWorkflowCoreDashboard("/workflows"));
                }))
            .StartAsync();
        return (host, host.GetTestClient());
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string id, string action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/workflows/api/instances/{id}/{action}");
        request.Headers.Add("X-Wfc-Dashboard", "1");
        return await client.SendAsync(request);
    }

    private static async Task AssertNotFound(HttpResponseMessage response, string id)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("instance-not-found", body.GetProperty("code").GetString());
        Assert.Equal($"Workflow instance '{id}' was not found.", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("suspend", false)]
    [InlineData("resume", false)]
    [InlineData("terminate", false)]
    [InlineData("suspend", true)]
    [InlineData("resume", true)]
    [InlineData("terminate", true)]
    public async Task Acting_on_an_unknown_instance_returns_404_instance_not_found(string action, bool providerReturnsNull)
    {
        var (host, client) = await Start(providerReturnsNull);
        using (host)
        using (client)
        {
            await AssertNotFound(await Post(client, "does-not-exist", action), "does-not-exist");
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Acting_on_a_known_instance_still_returns_success()
    {
        var (host, client) = await Start(nullProvider: false);
        using (host)
        using (client)
        {
            var store = host.Services.GetRequiredService<IPersistenceProvider>();
            var id = await store.CreateNewWorkflow(new WorkflowInstance
            {
                WorkflowDefinitionId = "Demo",
                Version = 1,
                Status = WorkflowStatus.Runnable,
                CreateTime = DateTime.UtcNow,
            });

            var response = await Post(client, id, "suspend");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.TryGetProperty("success", out _));
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Read_only_403_wins_over_unknown_instance_404()
    {
        var (host, client) = await Start(nullProvider: false, readOnly: true);
        using (host)
        using (client)
        {
            var response = await Post(client, "does-not-exist", "terminate");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await host.StopAsync();
        }
    }
}
