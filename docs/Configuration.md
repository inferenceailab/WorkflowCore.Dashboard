# Configuration

## Registering and mapping

```csharp
builder.Services.AddWorkflowCoreDashboard(options => { /* DashboardOptions */ })
    .UseEntityFrameworkJournal(db => db.UseNpgsql(cs), schema: "dashboard")   // optional
    .AddDesigner(designer => { /* DesignerOptions */ });                        // optional

app.MapWorkflowCoreDashboard("/workflows");
```

`MapWorkflowCoreDashboard(prefix)` maps the UI at `{prefix}`, the API at `{prefix}/api` and live updates at `{prefix}/hub`. It returns the route group, so you can add conventions such as `RequireAuthorization(...)`. The prefix cannot be `/`. The UI works under `UsePathBase` and behind proxies that keep the path.

## DashboardOptions

| Option | Default | Meaning |
|---|---|---|
| `Title` | `"Workflow Core"` | Shown in the header and the browser tab. |
| `AllowActions` | `true` | `false` makes the dashboard read-only: start, suspend, resume, terminate, publish event and the designer are refused. |
| `Authorization` | local requests only | `Func<HttpContext, bool>` deciding who may use the dashboard, API and hub. See [Security](Security.md). |
| `FrameAncestors` | `null` (no framing) | CSP `frame-ancestors` value for sites allowed to show the dashboard in a frame, e.g. `"'self' https://portal.example.com"`. |
| `MaxPageSize` | `200` | Largest page the instances endpoint returns. |
| `ActivityCapacity` | `1000` | In-memory journal only: activity entries kept. |
| `InMemoryIndexCapacity` | `10000` | In-memory journal only: instances kept in the index. |
| `JournalStepEvents` | `true` | Store step started/completed events. They are always shown live; turning this off keeps a database journal much smaller. |
| `JournalRetention` | 30 days | Persistent journals delete activity older than this, hourly. `TimeSpan.Zero` keeps everything. The instance index is kept. |
| `BackfillIndex` | `true` | Persistent journals: on first start, index instances that already exist. |
| `InstanceListing` | `Auto` | `Journal`, `Provider` or `Auto`. See [Activity journal](Activity-Journal.md#instance-listing). |

## Entity Framework journal

```csharp
.UseEntityFrameworkJournal(Action<DbContextOptionsBuilder> configure, string? schema = null)
```

- `configure`: the EF Core provider and connection, e.g. `db => db.UseSqlServer(cs)`. Any relational provider works: SQL Server, PostgreSQL (Npgsql), MySQL (Pomelo), SQLite, Oracle.
- `schema`: optional schema for the three `WfcDashboard_` tables.

The tables are created on first use, also in a database that already has other tables. See [Activity journal](Activity-Journal.md).

## DesignerOptions

| Option | Default | Meaning |
|---|---|---|
| `IncludeAssembly` | assemblies that reference Workflow Core | Which loaded assemblies are scanned for step types and data types. Workflow Core's own packages and the dashboard are excluded. |
| `StepAssemblies` | empty | Extra assemblies to scan, e.g. a library whose steps are only used from JSON/YAML: `designer.StepAssemblies.Add(typeof(SendEmail).Assembly)`. |
| `AllowAnyType` | `false` | `false`: definitions may only use the step and data types the catalog offers. `true`: any type name that resolves is accepted. |
| `SyncInterval` | 30 seconds | How often each node registers versions published on other nodes. |

The designer needs `AllowActions = true`. Its designs are stored in the journal, so use a persistent journal to keep them.

## Logging

Log categories start with `WorkflowCore.Dashboard`. Journal write failures are logged as warnings and retried; entries are dropped after repeated failures, never blocking workflows.
