# Security

**Anyone who can open the dashboard has administrative access to your workflows.** They can read workflow data and error stack traces, start, suspend, resume and terminate workflows, and publish events, which can push waiting workflows forward (approvals, shipments, payments). With the designer they can publish workflows that run any step type the designer offers, with inputs they choose.

Treat dashboard access like production admin access.

## Defaults

| Default | Effect |
|---|---|
| Local requests only | Requests must come from the same machine and name it by IP address, `localhost` or its machine name. |
| Actions allowed | Set `AllowActions = false` for a read-only dashboard. |
| No framing | Other sites cannot show the dashboard in a frame. |

## Exposing the dashboard

The dashboard uses your app's own sign-in. Its UI calls the API on the same origin, so any **cookie-based** scheme works: cookie authentication, OpenID Connect (Microsoft Entra ID, Auth0, Keycloak…) or Windows authentication. A bearer-token-only setup does not work for the UI, because the UI does not attach tokens to its requests.

One rule on the mapped route group protects everything: the UI (including deep links), the API and the live-updates hub.

### Admins only

```csharp
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie()
    .AddOpenIdConnect(options => { /* your identity provider */ });

builder.Services.AddAuthorization(o =>
    o.AddPolicy("WorkflowAdmins", p => p.RequireRole("workflow-admin")));

builder.Services.AddWorkflowCoreDashboard(options =>
{
    options.Authorization = _ => true;   // replaces the local-only default; the policy decides
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapWorkflowCoreDashboard("/workflows").RequireAuthorization("WorkflowAdmins");
```

Signed-out users are sent to the login page; signed-in users without the role get 403.

### Viewers and admins

`AllowActions = false` makes the dashboard read-only for everyone. To let some people watch and others act, decide per request instead: reads are GET, changes are POST, PUT or DELETE.

```csharp
builder.Services.AddWorkflowCoreDashboard(options =>
{
    options.Authorization = http =>
        http.User.IsInRole("workflow-admin")
        || (http.User.IsInRole("workflow-viewer")
            && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)));
});

app.MapWorkflowCoreDashboard("/workflows").RequireAuthorization();   // sign-in required
```

Viewers see the action buttons, but using them returns "access denied".

### Windows authentication (intranet)

```csharp
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(o =>
    o.AddPolicy("WorkflowAdmins", p => p.RequireRole(@"CONTOSO\Workflow Admins")));
```

Map the dashboard with `RequireAuthorization("WorkflowAdmins")` and `Authorization = _ => true`, as above.

### Scripts and other services

Add a bearer-token scheme next to the cookie one and allow it in the policy:

```csharp
builder.Services.AddAuthentication().AddJwtBearer(/* ... */);
builder.Services.AddAuthorization(o => o.AddPolicy("WorkflowAdmins", p => p
    .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme)
    .RequireRole("workflow-admin")));
```

Scripts then send `Authorization: Bearer …` and, for changes, `X-Wfc-Dashboard: 1`. See [REST API](REST-API.md).

### Also

- **Use HTTPS.** Workflow data and sign-in cookies travel over this connection.
- **Behind a reverse proxy, do not rely on the local-only default.** A proxy on the same machine makes every request look local. Configure sign-in as above, or `ForwardedHeaders` so the client address is known.
- **Do not enable a permissive CORS policy** (`AllowAnyOrigin` with credentials) for the dashboard path.
- **Keep `Authorization` in mind when adding `RequireAuthorization`.** Both apply. Leaving the local-only default in place keeps everyone else out even after they sign in.

These setups are covered by `AuthorizationTests`.

## Built-in protections

| Threat | Protection |
|---|---|
| Cross-site request forgery: another website makes your browser change workflows | Every change must carry the `X-Wfc-Dashboard` header, which browsers only send cross-origin after a CORS preflight the dashboard never approves. Requests marked `Sec-Fetch-Site: cross-site` are refused. |
| DNS rebinding: a website points its domain at 127.0.0.1 to reach a local dashboard | The local-only default also checks the `Host` header: only IP addresses, `localhost` and the machine name pass. |
| Clickjacking | `X-Frame-Options: DENY` and `Content-Security-Policy: frame-ancestors 'none'`, unless `FrameAncestors` is set. |
| MIME sniffing | `X-Content-Type-Options: nosniff`. |
| Unsafe YAML in the designer import | YAML is read as plain data; type tags (`!Type`) are refused, as are documents over 1 MB or with runaway aliases. |
| Designer creating arbitrary types | Step and data types must come from the designer's catalog unless `AllowAnyType` is set. |
| Hub access | The SignalR hub applies the same `Authorization` check when a client connects. |

## Data the dashboard stores and shows

- Workflow data, event payloads and step outputs are shown as JSON. If they contain personal data, so does the dashboard.
- Error stack traces are shown in the activity feed and, with a persistent journal, stored in the `WfcDashboard_Activity` table. They can reveal file paths and library versions.
- Activity is deleted after `JournalRetention` (30 days by default).

## Reporting a vulnerability

See [SECURITY.md](../SECURITY.md). Please report privately, not in an issue.
