using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace WorkflowCore.Dashboard.OpenIdConnect;

public enum DashboardRole
{
    /// <summary>No access.</summary>
    None,

    /// <summary>Sees everything, changes nothing.</summary>
    Viewer,

    /// <summary>Sees everything and can start, stop and change workflows, publish events and use the designer.</summary>
    Admin,
}

/// <summary>
/// Sign-in to the dashboard through an OpenID Connect identity provider. The provider-specific options
/// (<see cref="OktaOptions"/>, <see cref="EntraIdOptions"/>…) fill in the authority and claim names for you.
/// </summary>
public class DashboardOidcOptions
{
    /// <summary>Shown on sign-in pages, e.g. "Okta".</summary>
    public string ProviderName { get; set; } = "your identity provider";

    /// <summary>The issuer URL; its <c>/.well-known/openid-configuration</c> must be reachable.</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret of a confidential ("web") app. Keep it out of source control.</summary>
    public string? ClientSecret { get; set; }

    public List<string> Scopes { get; } = ["openid", "profile", "email"];

    /// <summary>
    /// The claim with the user's groups or roles, e.g. <c>groups</c>. A claim inside a JSON claim can be reached with
    /// a dotted path, e.g. <c>realm_access.roles</c>. <c>null</c> when the provider sends no groups.
    /// </summary>
    public string? GroupsClaim { get; set; } = "groups";

    /// <summary>The claim with the user's email address, used for <see cref="AdminUsers"/> and <see cref="AllowedDomains"/>.</summary>
    public string EmailClaim { get; set; } = "email";

    /// <summary>Members of these groups are admins.</summary>
    public List<string> AdminGroups { get; } = [];

    /// <summary>Members of these groups are viewers.</summary>
    public List<string> ViewerGroups { get; } = [];

    /// <summary>These email addresses are admins.</summary>
    public List<string> AdminUsers { get; } = [];

    /// <summary>These email addresses are viewers.</summary>
    public List<string> ViewerUsers { get; } = [];

    /// <summary>When set, only users with an email address in one of these domains get any role.</summary>
    public List<string> AllowedDomains { get; } = [];

    /// <summary>Role for signed-in users that no group or address above matches. <see cref="DashboardRole.None"/> denies them.</summary>
    public DashboardRole DefaultRole { get; set; } = DashboardRole.None;

    /// <summary>How long a dashboard session lasts without activity.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Only turn off for a local development identity provider on plain HTTP.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Last say over the OpenID Connect handler's settings, for anything not covered above.</summary>
    public Action<OpenIdConnectOptions>? ConfigureOpenIdConnect { get; set; }

    /// <summary>For providers whose logout is not standard OpenID Connect: builds the logout URL from the return URL.</summary>
    internal Func<string, string>? SignOutUrl { get; set; }

    internal virtual void ApplyProviderDefaults()
    {
    }

    /// <summary>Provider-specific checks on the account, on top of <see cref="AllowedDomains"/>.</summary>
    internal virtual bool AccountAllowed(ClaimsPrincipal user) => true;

    internal virtual IEnumerable<string> ValidateProvider() => [];

    internal IEnumerable<string> Validate()
    {
        foreach (var problem in ValidateProvider())
            yield return problem;
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority))
            yield return "Authority must be an absolute URL, such as https://login.example.com.";
        else if (RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
            yield return "Authority must use HTTPS (set RequireHttpsMetadata = false only for a local test provider).";
        if (string.IsNullOrWhiteSpace(ClientId))
            yield return "ClientId is required.";
        if (AdminGroups.Count + ViewerGroups.Count + AdminUsers.Count + ViewerUsers.Count == 0 && DefaultRole == DashboardRole.None)
            yield return "Nobody could use the dashboard: set AdminGroups, ViewerGroups, AdminUsers, ViewerUsers or DefaultRole.";
        if (AdminGroups.Count + ViewerGroups.Count > 0 && string.IsNullOrEmpty(GroupsClaim))
            yield return "AdminGroups and ViewerGroups need GroupsClaim: the claim your provider puts the groups in.";
    }
}

/// <summary>Okta. Groups come from a "groups" claim added to the authorization server.</summary>
public sealed class OktaOptions : DashboardOidcOptions
{
    public OktaOptions() => ProviderName = "Okta";

    /// <summary>Your Okta domain, e.g. <c>acme.okta.com</c>.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The custom authorization server, <c>default</c> unless you created another. Set to <c>null</c> to use the
    /// org authorization server, which only sends groups for the <c>groups</c> scope.
    /// </summary>
    public string? AuthorizationServerId { get; set; } = "default";

    internal override void ApplyProviderDefaults()
    {
        if (string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(Domain))
        {
            var host = Domain.Replace("https://", "").TrimEnd('/');
            Authority = string.IsNullOrEmpty(AuthorizationServerId) ? $"https://{host}" : $"https://{host}/oauth2/{AuthorizationServerId}";
        }
        if (string.IsNullOrEmpty(AuthorizationServerId) && !string.IsNullOrEmpty(GroupsClaim) && !Scopes.Contains("groups"))
            Scopes.Add("groups");
    }
}

/// <summary>Microsoft Entra ID (Azure AD). Roles come from app roles assigned to users or groups.</summary>
public sealed class EntraIdOptions : DashboardOidcOptions
{
    public EntraIdOptions()
    {
        ProviderName = "Microsoft Entra ID";
        GroupsClaim = "roles";
        EmailClaim = "preferred_username";
    }

    /// <summary>Directory (tenant) ID or domain, e.g. <c>contoso.onmicrosoft.com</c>.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Sign-in host; change for national clouds, e.g. <c>https://login.microsoftonline.us</c>.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com";

    internal override void ApplyProviderDefaults()
    {
        if (string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(TenantId))
            Authority = $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";
    }

    internal override IEnumerable<string> ValidateProvider()
    {
        // These accept accounts from any organization (or personal accounts): the dashboard is for one directory.
        if (TenantId.Trim().ToLowerInvariant() is "common" or "organizations" or "consumers")
            yield return $"TenantId \"{TenantId}\" lets accounts from other organizations sign in. Use your directory (tenant) ID.";
        else if (string.IsNullOrWhiteSpace(TenantId) && !Authority.Contains("/v2.0", StringComparison.Ordinal))
            yield return "TenantId is required: your directory (tenant) ID.";
    }
}

/// <summary>Auth0. Roles reach the ID token through an Action that adds a namespaced claim.</summary>
public sealed class Auth0Options : DashboardOidcOptions
{
    public Auth0Options()
    {
        ProviderName = "Auth0";
        GroupsClaim = null;
    }

    /// <summary>Your tenant or custom domain, e.g. <c>acme.eu.auth0.com</c>.</summary>
    public string Domain { get; set; } = string.Empty;

    internal override void ApplyProviderDefaults()
    {
        if (string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(Domain))
            Authority = $"https://{Domain.Replace("https://", "").TrimEnd('/')}/";
    }
}

/// <summary>Keycloak. Realm roles are read from <c>realm_access.roles</c>.</summary>
public sealed class KeycloakOptions : DashboardOidcOptions
{
    public KeycloakOptions()
    {
        ProviderName = "Keycloak";
        GroupsClaim = "realm_access.roles";
    }

    /// <summary>The Keycloak server, e.g. <c>https://sso.example.com</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Realm { get; set; } = string.Empty;

    internal override void ApplyProviderDefaults()
    {
        if (string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(BaseUrl))
            Authority = $"{BaseUrl.TrimEnd('/')}/realms/{Realm}";
    }
}

/// <summary>Google. There are no groups; use <see cref="DashboardOidcOptions.AdminUsers"/> and <see cref="DashboardOidcOptions.AllowedDomains"/>.</summary>
public sealed class GoogleOptions : DashboardOidcOptions
{
    public GoogleOptions()
    {
        ProviderName = "Google";
        Authority = "https://accounts.google.com";
        GroupsClaim = null;
    }

    /// <summary>
    /// With <see cref="DashboardOidcOptions.AllowedDomains"/>, the account must also belong to that Google Workspace
    /// (the <c>hd</c> claim). An email address alone is not enough: a personal Google account can be registered
    /// with a company address, and keeps working after its owner leaves the company.
    /// </summary>
    internal override bool AccountAllowed(ClaimsPrincipal user) =>
        AllowedDomains.Count == 0
        || (user.FindFirst("hd")?.Value is { } domain && AllowedDomains.Any(d => string.Equals(d.TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>Amazon Cognito user pools. Groups come from <c>cognito:groups</c>.</summary>
public sealed class CognitoOptions : DashboardOidcOptions
{
    public CognitoOptions()
    {
        ProviderName = "Amazon Cognito";
        GroupsClaim = "cognito:groups";
    }

    /// <summary>The user pool's region, e.g. <c>eu-west-1</c>.</summary>
    public string Region { get; set; } = string.Empty;

    public string UserPoolId { get; set; } = string.Empty;

    /// <summary>
    /// The user pool's domain, e.g. <c>acme.auth.eu-west-1.amazoncognito.com</c>. Needed for signing out,
    /// because Cognito's logout endpoint is not the standard one.
    /// </summary>
    public string? Domain { get; set; }

    internal override void ApplyProviderDefaults()
    {
        if (string.IsNullOrEmpty(Authority) && !string.IsNullOrEmpty(Region))
            Authority = $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}";
        if (!string.IsNullOrEmpty(Domain))
        {
            var host = Domain.Replace("https://", "").TrimEnd('/');
            SignOutUrl = returnUrl => $"https://{host}/logout?client_id={Uri.EscapeDataString(ClientId)}&logout_uri={Uri.EscapeDataString(returnUrl)}";
        }
    }
}
