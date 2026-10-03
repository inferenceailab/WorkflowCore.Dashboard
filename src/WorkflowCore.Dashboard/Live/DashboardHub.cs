using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace WorkflowCore.Dashboard.Live;

/// <summary>Server-to-client push only. Clients receive <c>activity</c> messages.</summary>
public sealed class DashboardHub : Hub
{
    private readonly DashboardOptions _options;

    public DashboardHub(IOptions<DashboardOptions> options)
    {
        _options = options.Value;
    }

    public override Task OnConnectedAsync()
    {
        // Endpoint filters do not cover hubs, so the dashboard authorization check is repeated here.
        var http = Context.GetHttpContext();
        if (http is null || !_options.Authorization(http))
            Context.Abort();

        return base.OnConnectedAsync();
    }
}
