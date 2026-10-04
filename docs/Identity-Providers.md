# Identity providers

The `WorkflowCore.Dashboard.OpenIdConnect` package adds sign-in to the dashboard in one call, with two roles:

- **Admin**: sees everything, starts, suspends, resumes and terminates workflows, publishes events, uses the designer.
- **Viewer**: sees everything, changes nothing. The UI hides the action buttons and the API refuses changes.

Everyone else is refused, with a page that shows who they are signed in as and which groups the identity provider sent.

| Provider | Method | Roles come from |
|---|---|---|
| [Okta](#okta) | `UseOkta` | Okta groups (`groups` claim) |
| [Microsoft Entra ID](#microsoft-entra-id) | `UseEntraId` | App roles (`roles` claim) |
| [Auth0](#auth0) | `UseAuth0` | Auth0 roles, through a login Action |
| [Keycloak](#keycloak) | `UseKeycloak` | Realm roles (`realm_access.roles`) or groups |
| [Google](#google) | `UseGoogle` | Email addresses and your Workspace domain |
| [Amazon Cognito](#amazon-cognito) | `UseCognito` | User pool groups (`cognito:groups`) |
| [Any other OpenID Connect provider](#other-providers) | `UseOpenIdConnect` | The claim you name |

All of them use OpenID Connect with the authorization code flow and PKCE. The package targets .NET 8, 9 and 10.

## Quick start with Okta

1. Install the package:

   ```shell
   dotnet add package WorkflowCore.Dashboard.OpenIdConnect
   ```

2. In the Okta Admin Console, create the app and the groups claim as described under [Okta](#okta). Note the client ID and secret.

3. Add sign-in to the dashboard:

   ```csharp
   using WorkflowCore.Dashboard.OpenIdConnect;

   builder.Services.AddWorkflowCoreDashboard()
       .UseOkta(okta =>
       {
           okta.Domain = "acme.okta.com";
           okta.ClientId = builder.Configuration["Okta:ClientId"]!;
           okta.ClientSecret = builder.Configuration["Okta:ClientSecret"];
           okta.AdminGroups.Add("Workflow Admins");
           okta.ViewerGroups.Add("Engineering");
       });

   var app = builder.Build();
   app.MapWorkflowCoreDashboard("/workflows");
   ```

4. Run the app and open `/workflows`. You are sent to Okta, then back to the page you asked for.

`WebApplication` adds the authentication and authorization middleware by itself. If your app calls `UseRouting()` explicitly, call `UseAuthentication()` and `UseAuthorization()` after it.

## How it works

- **URLs to register with the provider** (replace the host, and `/workflows` if you mapped the dashboard elsewhere):

  | Purpose | URL |
  |---|---|
  | Sign-in redirect (callback) | `https://your-app.example.com/workflows/signin-oidc` |
  | Sign-out redirect | `https://your-app.example.com/workflows/signout-callback-oidc` |

  Amazon Cognito is the exception for sign-out: see its section.

- **Its own session.** The dashboard keeps its own sign-in cookie, `.WorkflowCoreDashboard`, sent only to `/workflows`. Your app's sign-in, if it has one, is untouched. The cookie is `HttpOnly` and `SameSite=Lax`, holds the user's name, email and dashboard role, and lasts `SessionLifetime` (8 hours) after the last activity.
- **Roles are decided at sign-in.** A user whose groups change gets the new role at their next sign-in.
- **Sign-out** (account menu, top right) ends the dashboard session and the session at the provider.
- **Expired sessions**: API calls return `401` and the page reloads, which signs the user in again (often without a prompt, while the provider's session lasts).

### Choosing who gets which role

| Option | Meaning |
|---|---|
| `AdminGroups`, `ViewerGroups` | Values of the groups claim (`GroupsClaim`) that make someone an admin or viewer. Compared without case. |
| `AdminUsers`, `ViewerUsers` | Email addresses. Only used when the provider says the address is verified. |
| `AllowedDomains` | When set, only addresses in these domains get any role. |
| `DefaultRole` | Role for signed-in users nothing else matches: `None` (default, refused), `Viewer` or `Admin`. |

Admin wins when several rules match. For example, "the whole company can watch, the platform team can act":

```csharp
okta.DefaultRole = DashboardRole.Viewer;
okta.AdminGroups.Add("Platform Team");
```

`DefaultRole = Viewer` lets every account your provider lets through see the dashboard. Limit who can sign in to the app at the provider (most call this *assignment*) or with `AllowedDomains`.

## Settings instead of code

`UseSignIn` reads everything from configuration, so each environment can point at its own provider and the secret can come from an environment variable or a vault:

```csharp
builder.Services.AddWorkflowCoreDashboard()
    .UseSignIn(builder.Configuration.GetSection("Dashboard:SignIn"));
```

```json
{
  "Dashboard": {
    "SignIn": {
      "Provider": "Okta",
      "Domain": "acme.okta.com",
      "ClientId": "0oa1b2c3d4e5f6g7h8i9",
      "AdminGroups": [ "Workflow Admins" ],
      "ViewerGroups": [ "Engineering" ]
    }
  }
}
```

```shell
# Never commit the secret: set it in the environment (or user secrets, Key Vault, AWS Secrets Manager…).
export Dashboard__SignIn__ClientSecret='…'
```

`Provider` is one of `Okta`, `EntraId`, `Auth0`, `Keycloak`, `Google`, `Cognito` or `OpenIdConnect`. The other keys are the [options](#options) of that provider.

## Okta

1. **Create the app.** Admin Console → **Applications** → **Applications** → **Create App Integration** → **OIDC - OpenID Connect** → **Web Application**.
   - Sign-in redirect URIs: `https://your-app.example.com/workflows/signin-oidc`
   - Sign-out redirect URIs: `https://your-app.example.com/workflows/signout-callback-oidc`
   - **Assignments**: the people or groups allowed to sign in at all.
2. **Copy the client ID and client secret** from the app's **General** tab.
3. **Send the groups.** **Security** → **API** → **Authorization Servers** → `default` → **Claims** → **Add Claim**:
   - Name: `groups`
   - Include in token type: **ID Token**, **Always**
   - Value type: **Groups**
   - Filter: **Matches regex** `.*`, or **Starts with** `Workflow` to send only the relevant groups
4. Configure:

   ```csharp
   .UseOkta(okta =>
   {
       okta.Domain = "acme.okta.com";             // your Okta domain, or your custom domain
       okta.ClientId = "…";
       okta.ClientSecret = "…";
       okta.AdminGroups.Add("Workflow Admins");    // Okta group names
       okta.ViewerGroups.Add("Engineering");
   })
   ```

**No custom authorization server?** Okta orgs without API Access Management only have the org authorization server. Put the groups claim on the app instead (**Sign On** tab → **OpenID Connect ID Token** → **Groups claim type** `Filter`, **Groups claim filter** `groups` *Matches regex* `.*`) and set `okta.AuthorizationServerId = null`. The dashboard then also asks for the `groups` scope.

## Microsoft Entra ID

Use **app roles**: their values arrive in the `roles` claim by name. The `groups` claim carries group object IDs (GUIDs), and is left out for users in more than 200 groups.

1. **Register the app.** Entra admin center → **App registrations** → **New registration**.
   - Supported account types: **Accounts in this organizational directory only**.
   - Redirect URI, platform **Web**: `https://your-app.example.com/workflows/signin-oidc`
   - Then, under **Authentication**, add a second **Web** redirect URI: `https://your-app.example.com/workflows/signout-callback-oidc`
2. **Create a secret**: **Certificates & secrets** → **New client secret**. Copy the *value*.
3. **Create the roles**: **App roles** → **Create app role**, twice:
   - Display name `Workflow admin`, allowed member types **Users/Groups**, value `Workflow.Admin`
   - Display name `Workflow viewer`, allowed member types **Users/Groups**, value `Workflow.Viewer`
4. **Assign them**: **Enterprise applications** → your app → **Users and groups** → **Add user/group**, pick the role. Assigning groups needs Entra ID P1 or P2. Set **Properties** → **Assignment required** to **Yes** to keep everyone else from signing in.
5. Configure:

   ```csharp
   .UseEntraId(entra =>
   {
       entra.TenantId = "…";                       // Directory (tenant) ID
       entra.ClientId = "…";                       // Application (client) ID
       entra.ClientSecret = "…";
       entra.AdminGroups.Add("Workflow.Admin");    // app role values
       entra.ViewerGroups.Add("Workflow.Viewer");
   })
   ```

`TenantId` must be your directory. `common`, `organizations` and `consumers` are refused at startup: they would let accounts from other organizations sign in. For national clouds, set `Instance`, e.g. `https://login.microsoftonline.us`.

## Auth0

1. **Create the app.** **Applications** → **Create Application** → **Regular Web Applications**. In **Settings**:
   - Allowed Callback URLs: `https://your-app.example.com/workflows/signin-oidc`
   - Allowed Logout URLs: `https://your-app.example.com/workflows/signout-callback-oidc`
   - Copy the **Domain**, **Client ID** and **Client Secret**.
2. **Create the roles**: **User Management** → **Roles**: `workflow-admin` and `workflow-viewer`. Assign them to users.
3. **Put the roles in the ID token.** Auth0 leaves them out by default. **Actions** → **Library** → **Create Action** → trigger **Login / Post Login**:

   ```javascript
   exports.onExecutePostLogin = async (event, api) => {
     // Custom claims need a URL-like name; it does not have to resolve.
     api.idToken.setCustomClaim('https://workflows.example.com/roles', event.authorization?.roles ?? []);
   };
   ```

   **Deploy** it, then add it to the **post-login** trigger (**Actions** → **Triggers**).
4. Configure:

   ```csharp
   .UseAuth0(auth0 =>
   {
       auth0.Domain = "acme.eu.auth0.com";         // or your custom domain
       auth0.ClientId = "…";
       auth0.ClientSecret = "…";
       auth0.GroupsClaim = "https://workflows.example.com/roles";
       auth0.AdminGroups.Add("workflow-admin");
       auth0.ViewerGroups.Add("workflow-viewer");
   })
   ```

Sign-out uses Auth0's OpenID Connect logout endpoint. Tenants created before November 2023 may need **Settings** → **Advanced** → **RP-Initiated Logout End Session Endpoint Discovery** turned on.

## Keycloak

1. **Create the client.** Your realm → **Clients** → **Create client**: type **OpenID Connect**, client ID `workflow-dashboard`, **Client authentication** on, **Standard flow** on.
   - Valid redirect URIs: `https://your-app.example.com/workflows/signin-oidc`
   - Valid post logout redirect URIs: `https://your-app.example.com/workflows/signout-callback-oidc`
   - Copy the secret from the **Credentials** tab.
2. **Create the roles**: **Realm roles** → `workflow-admin` and `workflow-viewer`. Assign them to users or groups.
3. **Put the roles in the ID token.** Keycloak only puts them in the access token by default. **Client scopes** → `roles` → **Mappers** → `realm roles` → turn on **Add to ID token** and **Add to userinfo**.
4. Configure:

   ```csharp
   .UseKeycloak(keycloak =>
   {
       keycloak.BaseUrl = "https://sso.example.com";
       keycloak.Realm = "ops";
       keycloak.ClientId = "workflow-dashboard";
       keycloak.ClientSecret = "…";
       keycloak.AdminGroups.Add("workflow-admin");     // realm role names
       keycloak.ViewerGroups.Add("workflow-viewer");
   })
   ```

**Groups instead of roles:** add a **Group Membership** mapper (claim name `groups`, **Full group path** off, **Add to ID token** on) and set `keycloak.GroupsClaim = "groups"`. **Client roles:** set `GroupsClaim = "resource_access.workflow-dashboard.roles"` and turn on **Add to ID token** for the `client roles` mapper.

## Google

Google has no groups in its tokens, so roles come from email addresses and your Google Workspace domain.

1. **Create the client.** Google Cloud console → **APIs & Services** → **OAuth consent screen**: user type **Internal** (only your Workspace). Then **Credentials** → **Create credentials** → **OAuth client ID** → **Web application**.
   - Authorized redirect URIs: `https://your-app.example.com/workflows/signin-oidc`
2. Configure:

   ```csharp
   .UseGoogle(google =>
   {
       google.ClientId = "….apps.googleusercontent.com";
       google.ClientSecret = "…";
       google.AllowedDomains.Add("example.com");     // your Workspace domain
       google.DefaultRole = DashboardRole.Viewer;    // everyone in it can watch
       google.AdminUsers.Add("ada@example.com");     // these people can act
   })
   ```

With `AllowedDomains`, the account must also be managed by that Workspace (Google's `hd` claim). An address alone is not enough: anyone can register a personal Google account with a company address, and it keeps working after they leave.

Signing out ends the dashboard session only. Google has no sign-out endpoint for apps.

## Amazon Cognito

1. **Create the app client.** Your user pool → **App integration** → **App clients** → **Create app client**: type **Traditional web application** (confidential, with a client secret).
   - Allowed callback URLs: `https://your-app.example.com/workflows/signin-oidc`
   - Allowed sign-out URLs: `https://your-app.example.com/workflows/signed-out`
   - OAuth grant type **Authorization code**, scopes `openid`, `email`, `profile`.
2. **Note the domain** of the user pool (**Branding** → **Domain**, e.g. `acme.auth.eu-west-1.amazoncognito.com`). It is needed for signing out, because Cognito's logout is not the standard one.
3. **Create the groups**: **Groups** → `workflow-admin` and `workflow-viewer`. Add users. Cognito puts them in the ID token as `cognito:groups`.
4. Configure:

   ```csharp
   .UseCognito(cognito =>
   {
       cognito.Region = "eu-west-1";
       cognito.UserPoolId = "eu-west-1_AbC123dEf";
       cognito.Domain = "acme.auth.eu-west-1.amazoncognito.com";
       cognito.ClientId = "…";
       cognito.ClientSecret = "…";
       cognito.AdminGroups.Add("workflow-admin");
       cognito.ViewerGroups.Add("workflow-viewer");
   })
   ```

## Other providers

`UseOpenIdConnect` works with any OpenID Connect provider. Give it the issuer URL and the name of the claim that carries groups or roles:

```csharp
.UseOpenIdConnect(oidc =>
{
    oidc.ProviderName = "PingOne";                          // shown on error pages
    oidc.Authority = "https://auth.pingone.com/<environment-id>/as";
    oidc.ClientId = "…";
    oidc.ClientSecret = "…";
    oidc.GroupsClaim = "groups";
    oidc.AdminGroups.Add("Workflow Admins");
})
```

| Provider | `Authority` | Roles or groups |
|---|---|---|
| PingOne | `https://auth.pingone.com/<environment-id>/as` | Add a `groups` attribute (group names) to the app's attribute mappings |
| PingFederate | your PingFederate base URL | Map group membership to an ID token claim in the OpenID Connect policy |
| OneLogin | `https://<subdomain>.onelogin.com/oidc/2` | App → **Parameters** → **Groups**, sent as `groups` |
| JumpCloud | `https://oauth.id.jumpcloud.com/` | App → **Include group attribute**; set `GroupsClaim` to the attribute name |
| Zitadel | `https://<instance>.zitadel.cloud` | Turn on **User roles inside ID Token**; `GroupsClaim = "urn:zitadel:iam:org:project:roles"` |
| Authentik | `https://authentik.example.com/application/o/<app-slug>/` | `groups`, sent with the `profile` scope |

`GroupsClaim` accepts:

- a claim sent once per group, or as a JSON array (`groups`);
- a JSON object whose keys are the roles (Zitadel);
- a path into a JSON claim (`realm_access.roles`);
- a URL-like name (`https://example.com/roles`).

To ask for more scopes, add them to `Scopes`. For anything else, `ConfigureOpenIdConnect` gives you the underlying `OpenIdConnectOptions`.

## Options

Common to every provider:

| Option | Default | Meaning |
|---|---|---|
| `ClientId` | (required) | The app's client ID. |
| `ClientSecret` | `null` | The app's client secret. Keep it out of source control. |
| `Authority` | set by the provider method | Issuer URL. Only needed with `UseOpenIdConnect`. |
| `GroupsClaim` | per provider | Claim with groups or roles; `null` when the provider sends none. |
| `EmailClaim` | `email` (`preferred_username` for Entra ID) | Claim with the email address. |
| `AdminGroups`, `ViewerGroups` | empty | See [Choosing who gets which role](#choosing-who-gets-which-role). |
| `AdminUsers`, `ViewerUsers` | empty | Email addresses. |
| `AllowedDomains` | empty | Email domains allowed any role. |
| `DefaultRole` | `None` | Role when nothing else matches. |
| `Scopes` | `openid`, `profile`, `email` | Scopes requested. |
| `SessionLifetime` | 8 hours | Session length without activity. |
| `RequireHttpsMetadata` | `true` | Only turn off for a local test provider on plain HTTP. |
| `ConfigureOpenIdConnect` | `null` | Last say over the ASP.NET Core `OpenIdConnectOptions`. |

Per provider:

| Provider | Options |
|---|---|
| Okta | `Domain`, `AuthorizationServerId` (`default`; `null` for the org authorization server) |
| Entra ID | `TenantId`, `Instance` (`https://login.microsoftonline.com`) |
| Auth0 | `Domain` |
| Keycloak | `BaseUrl`, `Realm` |
| Cognito | `Region`, `UserPoolId`, `Domain` |

Mistakes such as a missing client ID, an HTTP authority, or rules that let nobody in stop the app at startup with a message naming the provider and the option.

## Production checklist

- **HTTPS.** Providers refuse plain HTTP redirect URLs outside `localhost`.
- **Behind a reverse proxy or load balancer** that terminates TLS, forward the original scheme and host, or the redirect URL sent to the provider starts with `http://`:

  ```csharp
  builder.Services.Configure<ForwardedHeadersOptions>(o =>
  {
      o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
      o.KnownProxies.Add(IPAddress.Parse("10.0.0.10"));   // your proxy
  });
  app.UseForwardedHeaders();   // before the dashboard
  ```

- **Several instances, or containers that restart:** share ASP.NET Core Data Protection keys (`AddDataProtection().PersistKeysTo…`). The session cookie and the sign-in state are encrypted with them. Without shared keys, users are signed out at random or sign-in fails with "Correlation failed".
- **Client secret** in environment variables or a secret store, never in `appsettings.json` in source control.

## Troubleshooting

| What you see | Cause and fix |
|---|---|
| "You don't have access" | The page lists the groups the provider sent. Empty: the groups claim is missing; see your provider's section. Names differ: fix `AdminGroups`/`ViewerGroups`. The server also logs a warning with the claim it received. |
| The provider says the redirect URI is invalid | Register the exact sign-in URL above. Behind a proxy, check the URL in the address bar at the provider: if it says `http://`, see forwarded headers above. |
| "Correlation failed" after sign-in | The browser dropped the sign-in cookie: mixed `http`/`https`, several instances without shared Data Protection keys, or a sign-in that took longer than 15 minutes. |
| Sign-out shows an error at the provider | Register the sign-out redirect URL above (for Cognito, `/workflows/signed-out`). |
| Endless redirects | Another middleware or policy refuses the dashboard's own cookie. Do not add `RequireAuthorization(...)` with a different scheme on the dashboard group; the package sets the policy for you. |

## Try it locally with Keycloak

The repository has a ready Keycloak realm with three users:

```shell
docker compose -f samples/keycloak/docker-compose.yml up -d
dotnet run --project samples/WorkflowCore.Dashboard.Sample --launch-profile Keycloak
```

Open <http://localhost:5290/workflows> and sign in as:

| User | Password | Role |
|---|---|---|
| `ada` | `ada` | Admin |
| `val` | `val` | Viewer |
| `mo` | `mo` | None: sees the "access denied" page |

The sample reads its sign-in settings from `appsettings.Keycloak.json`. Point `Dashboard:SignIn` at your own provider to try that one.
