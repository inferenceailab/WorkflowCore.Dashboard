using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WorkflowCore.Dashboard.OpenIdConnect;

/// <summary>Turns what the identity provider says about a user into a dashboard role.</summary>
internal static class DashboardRoles
{
    public static bool HasAccess(HttpContext http) => http.User.HasClaim(c => c.Type == DashboardClaims.Role);

    public static bool IsAdmin(HttpContext http) => http.User.HasClaim(DashboardClaims.Role, DashboardClaims.Admin);

    public static (DashboardRole Role, IReadOnlyList<string> Groups) Resolve(ClaimsPrincipal user, DashboardOidcOptions options)
    {
        var groups = GroupsOf(user, options.GroupsClaim);
        var email = VerifiedEmail(user, options.EmailClaim);

        if (options.AllowedDomains.Count > 0
            && (email is null || !options.AllowedDomains.Any(d => email.EndsWith("@" + d.TrimStart('@'), StringComparison.OrdinalIgnoreCase))))
        {
            return (DashboardRole.None, groups);
        }
        if (!options.AccountAllowed(user))
            return (DashboardRole.None, groups);

        bool InGroup(List<string> wanted) => wanted.Any(g => groups.Contains(g, StringComparer.OrdinalIgnoreCase));
        bool IsUser(List<string> wanted) => email is not null && wanted.Contains(email, StringComparer.OrdinalIgnoreCase);

        if (InGroup(options.AdminGroups) || IsUser(options.AdminUsers))
            return (DashboardRole.Admin, groups);
        if (InGroup(options.ViewerGroups) || IsUser(options.ViewerUsers))
            return (DashboardRole.Viewer, groups);
        return (options.DefaultRole, groups);
    }

    /// <summary>
    /// Values of the groups claim. Handles one claim per group, a JSON array in one claim, a JSON object whose keys
    /// are the roles (Zitadel), and a dotted path into a JSON claim (<c>realm_access.roles</c>). A claim type that
    /// itself contains dots (Auth0's namespaced <c>https://example.com/roles</c>) is matched as a whole first.
    /// </summary>
    internal static List<string> GroupsOf(ClaimsPrincipal user, string? claim)
    {
        if (string.IsNullOrEmpty(claim))
            return [];

        var direct = user.FindAll(claim).Select(c => c.Value).ToList();
        if (direct.Count > 0)
            return direct.SelectMany(Expand).Distinct(StringComparer.Ordinal).ToList();

        for (var dot = claim.IndexOf('.'); dot > 0; dot = claim.IndexOf('.', dot + 1))
        {
            var root = user.FindFirst(claim[..dot])?.Value;
            if (root is null)
                continue;
            try
            {
                using var json = JsonDocument.Parse(root);
                var element = json.RootElement;
                foreach (var segment in claim[(dot + 1)..].Split('.'))
                {
                    if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                        return [];
                }
                return Values(element).ToList();
            }
            catch (JsonException)
            {
                return [];
            }
        }
        return [];
    }

    private static IEnumerable<string> Expand(string value)
    {
        if (!value.StartsWith('[') && !value.StartsWith('{'))
            return [value];
        try
        {
            using var json = JsonDocument.Parse(value);
            return Values(json.RootElement).ToList();
        }
        catch (JsonException)
        {
            return [value];
        }
    }

    private static IEnumerable<string> Values(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!),
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Object => element.EnumerateObject().Select(p => p.Name),
        _ => [],
    };

    /// <summary>The email address, unless the provider says it is unverified: an unverified address must not grant a role.</summary>
    private static string? VerifiedEmail(ClaimsPrincipal user, string claim)
    {
        if (string.Equals(user.FindFirst("email_verified")?.Value, "false", StringComparison.OrdinalIgnoreCase))
            return null;
        var email = user.FindFirst(claim)?.Value;
        return email is not null && email.Contains('@') ? email : null;
    }
}
