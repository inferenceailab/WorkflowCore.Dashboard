# Workflow Core Dashboard

An embeddable monitoring dashboard for [Workflow Core](https://github.com/danielgerlag/workflow-core), in the spirit of Elsa Studio. You add it to your ASP.NET Core app the same way you add the Hangfire dashboard.

- **Overview**: live lifecycle events, plus totals for the last hour, day or week
- **Definitions**: every registered workflow (C# and JSON/YAML) as a flowchart and as a step table
- **Instances**: newest first with totals; filter by status, definition and date; open any instance by ID
- **Instance detail**: flowchart with executed steps and taken paths highlighted, step timeline, workflow data, per-step payloads, error stack traces
- **Activity history**: stored in your database when you configure a persistent journal
- **Actions**: start, suspend, resume, terminate, publish events (can be turned off)

The UI is Angular 22 + Angular Material. It is compiled into the `WorkflowCore.Dashboard` assembly, so the host app serves no static files of its own.

## Run the sample

```bash
dotnet run --project samples/WorkflowCore.Dashboard.Sample
```

Open http://localhost:5290/workflows. The sample uses SQLite for both Workflow Core and the dashboard journal. It registers three C# workflows plus one JSON and one YAML definition, and starts new workflows every few seconds. Set `Sample:GenerateTraffic` to `false` in `appsettings.json` to turn that off.

The first build runs `npm ci` and `npm run build` for the UI, which needs Node.js 22.22+ or 24.15+ (Angular 22 requirement).

## Add it to your app

```csharp
builder.Services.AddWorkflow(...);              // your existing Workflow Core setup
builder.Services.AddWorkflowCoreDashboard(options =>
{
    options.Title = "Orders";
    options.AllowActions = true;                // false = read-only
});

var app = builder.Build();
app.MapWorkflowCoreDashboard("/workflows");     // UI, /workflows/api and /workflows/hub
```

### Keep activity history (recommended)

Without further setup, activity is kept in memory and lost on restart. Add the `WorkflowCore.Dashboard.EntityFramework` package to store it in any EF Core relational database:

```csharp
builder.Services.AddWorkflowCoreDashboard()
    .UseEntityFrameworkJournal(db => db.UseSqlServer(connectionString));   // or UseNpgsql, UseMySql, UseSqlite, UseOracle
```

- It can use the same database as Workflow Core. It creates three tables prefixed `WfcDashboard_` on first use; pass `schema: "dashboard"` to put them in their own schema.
- It is built against EF Core 8 and tested on EF Core 8, 9 and 10, so it uses whichever version your app already has.
- On first start it adds instances that already exist (backfill), so the list is complete from day one.
- With a distributed lifecycle event hub, every node can record events; each event is stored once.

| Option | Default | What it does |
|---|---|---|
| `JournalRetention` | 30 days | Activity older than this is deleted hourly. `TimeSpan.Zero` keeps everything. |
| `JournalStepEvents` | `true` | Store step started/completed events. Turn off to keep the journal small; they are still shown live. |
| `BackfillIndex` | `true` | Index existing instances on first start. |
| `InstanceListing` | `Auto` | `Journal`: newest first with totals. `Provider`: Workflow Core's own listing. `Auto`: the journal when it is persistent. |

### Security

By default the dashboard only answers **local requests**. Before you expose it, either replace the check:

```csharp
options.Authorization = http => http.User.IsInRole("Ops");
```

or use ASP.NET Core authorization on the returned route group:

```csharp
app.MapWorkflowCoreDashboard("/workflows").RequireAuthorization("OpsPolicy");
```

## Persistence providers

The dashboard only uses Workflow Core's own abstractions (`IPersistenceProvider`, `IWorkflowRegistry`, `IWorkflowController`, lifecycle events), so it works with whatever provider the host configures. The journal database is independent of it.

| Workflow Core provider | Without a persistent journal | With a persistent journal |
|---|---|---|
| SQL Server, PostgreSQL, MySQL, SQLite, Oracle, MongoDB, RavenDB, Azure Table Storage, in-memory | Instance list in storage order, no totals | Newest first with totals |
| Redis, Cosmos DB, DynamoDB | Only instances seen since the app started | Newest first with totals, for every instance started while the dashboard was installed |

These three providers cannot list instances, so backfill skips them.

## How the graph is drawn

Each definition is laid out top to bottom with [dagre](https://github.com/dagrejs/dagre). Container steps (If, While, ForEach, parallel Sequence, Saga) are drawn as boxes around their branches. Dashed lines lead into branches, dotted lines return from branch ends to the container's exit point, and red dashed lines lead to compensation steps.

On an instance, each step takes the color of its latest execution. A ×N badge shows steps that ran several times, for example inside a ForEach. Paths come from each execution pointer's predecessor, so the highlighted edges are the ones the instance actually took.

## Develop the UI

```bash
# terminal 1: API
dotnet run --project samples/WorkflowCore.Dashboard.Sample

# terminal 2: UI with hot reload, proxied to the API on :5290
cd src/WorkflowCore.Dashboard.UI
npm start                       # http://localhost:4200/workflows
```

`npm run build` writes into `src/WorkflowCore.Dashboard/wwwroot`, which is embedded on the next `dotnet build`. Use `-p:BuildDashboardUi=true` to force a UI rebuild from `dotnet build`, or `-p:SkipDashboardUi=true` to build the API without the UI.

## Tests

```bash
dotnet test                                   # EF Core 9
dotnet test -p:EfVersion=8.0.*                # also run against EF Core 8 and 10
dotnet test -p:EfVersion=10.0.*
```

## Layout

```
src/WorkflowCore.Dashboard/                 ASP.NET Core library: API, SignalR hub, in-memory journal, embedded UI host
src/WorkflowCore.Dashboard.EntityFramework/ Persistent journal on any EF Core relational database
src/WorkflowCore.Dashboard.UI/              Angular app
samples/WorkflowCore.Dashboard.Sample/
tests/WorkflowCore.Dashboard.Tests/
```

## Roadmap

- **Phase 3**: visual designer for JSON/YAML definitions. C# workflows stay view-only.
