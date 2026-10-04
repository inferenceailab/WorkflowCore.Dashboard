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

Use your app's authentication and an authorization policy:

```csharp
builder.Services.AddAuthorization(o =>
    o.AddPolicy("WorkflowAdmins", p => p.RequireRole("workflow-admin")));

builder.Services.AddWorkflowCoreDashboard(options =>
{
    options.Authorization = _ => true;   // the policy below decides
});

app.MapWorkflowCoreDashboard("/workflows").RequireAuthorization("WorkflowAdmins");
```

`RequireAuthorization` also covers the live-updates hub, because it is mapped in the same route group. Alternatively, decide in code:

```csharp
options.Authorization = http => http.User.IsInRole("workflow-admin");
```

Also:

- **Use HTTPS.** Workflow data and cookies travel over this connection.
- **Behind a reverse proxy, do not rely on the local-only default.** A proxy on the same machine makes every request look local. Configure real authorization, or `ForwardedHeaders` so the client address is known.
- **Do not enable a permissive CORS policy** (`AllowAnyOrigin` with credentials) for the dashboard path.
- **Consider read-only.** Teams that only monitor can use `AllowActions = false` on a separate, wider-access mapping.

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
