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

    /// <summary>In-memory journal only: number of activity entries kept.</summary>
    public int ActivityCapacity { get; set; } = 1000;

    /// <summary>In-memory journal only: number of instances kept in the index.</summary>
    public int InMemoryIndexCapacity { get; set; } = 10_000;

    /// <summary>
    /// Store StepStarted/StepCompleted events in the journal. They are always shown live; turning this off
    /// keeps a persistent journal much smaller.
    /// </summary>
    public bool JournalStepEvents { get; set; } = true;

    /// <summary>Persistent journals delete activity older than this. Zero keeps everything.</summary>
    public TimeSpan JournalRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Persistent journals: on first start, add instances that already exist in the persistence provider
    /// to the instance index.
    /// </summary>
    public bool BackfillIndex { get; set; } = true;

    /// <summary>Where the instances list comes from. See <see cref="InstanceListingSource"/>.</summary>
    public InstanceListingSource InstanceListing { get; set; } = InstanceListingSource.Auto;

    /// <summary>Largest page size the instances endpoint accepts.</summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Decides whether a request may use the dashboard. Defaults to local requests only.
    /// Replace it, or call <c>RequireAuthorization()</c> on the mapped group, before exposing the dashboard.
    /// </summary>
    public Func<HttpContext, bool> Authorization { get; set; } = IsLocalRequest;

    /// <summary>
    /// Decides whether a request that passed <see cref="Authorization"/> may also change things: start, suspend,
    /// resume, terminate, publish events, and the designer. <c>null</c> lets everyone with access act.
    /// Users who may not act see the dashboard without its action buttons.
    /// </summary>
    public Func<HttpContext, bool>? ActionAuthorization { get; set; }

    /// <summary>
    /// An ASP.NET Core authorization policy applied to the whole dashboard (UI, API and hub),
    /// the same as calling <c>RequireAuthorization(policy)</c> on the mapped group.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Target of the UI's sign-out link: relative to the dashboard (<c>"signout"</c>) or an absolute path
    /// (<c>"/account/logout"</c>). <c>null</c> hides the link.
    /// </summary>
    public string? SignOutPath { get; set; }

    /// <summary>
    /// Origins allowed to show the dashboard in a frame, as a CSP <c>frame-ancestors</c> value such as
    /// <c>'self' https://portal.example.com</c>. By default no site may frame it, which prevents clickjacking.
    /// </summary>
    public string? FrameAncestors { get; set; }

    /// <summary>
    /// True for requests from this machine, addressed to it by IP address, <c>localhost</c> or its machine name.
    /// </summary>
    public static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        var fromThisMachine = remote is null || IPAddress.IsLoopback(remote) || remote.Equals(context.Connection.LocalIpAddress);
        return fromThisMachine && IsLocalHostName(context.Request.Host.Host);
    }

    // A web page can point its own domain at 127.0.0.1 (DNS rebinding) and then reach the dashboard as if it were
    // local. Its requests still name that domain in the Host header, so only addresses and local names pass.
    private static bool IsLocalHostName(string host) =>
        string.IsNullOrEmpty(host)
        || IPAddress.TryParse(host.Trim('[', ']'), out _)
        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}

public enum InstanceListingSource
{
    /// <summary>
    /// The journal's instance index when the journal is persistent, otherwise the persistence provider,
    /// falling back to the index for providers that cannot list instances.
    /// </summary>
    Auto,

    /// <summary>Workflow Core's own listing: complete, but in storage order and without totals.</summary>
    Provider,

    /// <summary>The journal's instance index: newest first with totals, covering instances the journal has seen.</summary>
    Journal,
}
