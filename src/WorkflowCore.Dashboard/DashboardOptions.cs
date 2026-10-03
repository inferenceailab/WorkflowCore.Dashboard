using System.Net;
using Microsoft.AspNetCore.Http;

namespace WorkflowCore.Dashboard;

public class DashboardOptions
{
    /// <summary>Title shown in the dashboard header.</summary>
    public string Title { get; set; } = "Workflow Core";

    /// <summary>
    /// When false the dashboard is read-only: start, suspend, resume, terminate and publish are rejected.
    /// </summary>
    public bool AllowActions { get; set; } = true;

    /// <summary>Number of lifecycle events kept in memory for the activity feed.</summary>
    public int ActivityCapacity { get; set; } = 1000;

    /// <summary>Largest page size the instances endpoint accepts.</summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Decides whether a request may use the dashboard. Defaults to local requests only.
    /// Replace it, or call <c>RequireAuthorization()</c> on the mapped group, before exposing the dashboard.
    /// </summary>
    public Func<HttpContext, bool> Authorization { get; set; } = IsLocalRequest;

    public static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null)
            return true;

        return IPAddress.IsLoopback(remote) || remote.Equals(context.Connection.LocalIpAddress);
    }
}
