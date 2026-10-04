namespace WorkflowCore.Dashboard;

/// <summary>
/// Claims the dashboard reads from the signed-in user. Sign-in add-ons set them; apps with their own sign-in
/// can add them too, so the UI shows the user's role.
/// </summary>
public static class DashboardClaims
{
    /// <summary>The user's dashboard role: <see cref="Admin"/> or <see cref="Viewer"/>.</summary>
    public const string Role = "wfc_role";

    public const string Admin = "admin";

    public const string Viewer = "viewer";

    /// <summary>Groups received from the identity provider, kept to explain an "access denied" page.</summary>
    public const string Group = "wfc_group";
}

/// <summary>Where the dashboard is mapped, known once <c>MapWorkflowCoreDashboard</c> has run.</summary>
public sealed class DashboardRoute
{
    /// <summary>The path prefix, e.g. <c>/workflows</c>; <c>null</c> until the dashboard is mapped.</summary>
    public string? Prefix { get; internal set; }
}
