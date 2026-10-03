# Workflow Core Dashboard

An embeddable monitoring dashboard for [Workflow Core](https://github.com/danielgerlag/workflow-core), in the spirit of Elsa Studio. You add it to your ASP.NET Core app the same way you add the Hangfire dashboard.

- **Overview**: live lifecycle events, plus totals since the app started
- **Definitions**: every registered workflow (C# and JSON/YAML), its steps and default data
- **Instances**: filter by status, definition and date; open any instance by ID
- **Instance detail**: step timeline, workflow data, per-step payloads, error stack traces
- **Actions**: start, suspend, resume, terminate, publish events (can be turned off)

The UI is Angular 22 + Angular Material. It is compiled into the `WorkflowCore.Dashboard` assembly, so the host app serves no static files of its own.

## Run the sample

```bash
dotnet run --project samples/WorkflowCore.Dashboard.Sample
```

Open http://localhost:5290/workflows. The sample uses SQLite, registers three C# workflows plus one JSON and one YAML definition, and starts new workflows every few seconds. Set `Sample:GenerateTraffic` to `false` in `appsettings.json` to turn that off.

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

The dashboard only uses Workflow Core's own abstractions (`IPersistenceProvider`, `IWorkflowRegistry`, `IWorkflowController`, lifecycle events), so it works with whatever provider the host configures.

| Provider | Instance list | Open by ID | Everything else |
|---|---|---|---|
| SQL Server, PostgreSQL, MySQL, SQLite, Oracle (EF) | Yes | Yes | Yes |
| MongoDB, RavenDB, Azure Table Storage, in-memory | Yes | Yes | Yes |
| Redis, Cosmos DB, DynamoDB | No (provider throws `NotImplementedException`) | Yes | Yes |

Known limits of the provider listing API:

- Pages come back in storage order, not newest first. The UI sorts within each page.
- There is no total count, so paging is next/previous only.

## How live updates and activity work

`LifeCycleRelay` subscribes to `IWorkflowHost.OnLifeCycleEvent` and `OnStepError`, keeps the most recent events in memory (`ActivityCapacity`, default 1000) and pushes each one to the browser over SignalR.

Workflow Core has no execution journal of its own, so **activity history starts when the app starts and is lost on restart**. Step start/end times, retries and statuses come from the persisted execution pointers and are always available.

## Develop the UI

```bash
# terminal 1: API
dotnet run --project samples/WorkflowCore.Dashboard.Sample

# terminal 2: UI with hot reload, proxied to the API on :5290
cd src/WorkflowCore.Dashboard.UI
npm start                       # http://localhost:4200/workflows
```

`npm run build` writes into `src/WorkflowCore.Dashboard/wwwroot`, which is embedded on the next `dotnet build`. Use `-p:BuildDashboardUi=true` to force a UI rebuild from `dotnet build`, or `-p:SkipDashboardUi=true` to build the API without the UI.

## Layout

```
src/WorkflowCore.Dashboard/      ASP.NET Core library: API, SignalR hub, embedded UI host
src/WorkflowCore.Dashboard.UI/   Angular app
samples/WorkflowCore.Dashboard.Sample/
```

## Roadmap

- **Phase 2**: definition graph with executed steps highlighted, and a persisted activity journal. The journal also gives the Redis, Cosmos DB and DynamoDB providers an instance list, plus newest-first paging with totals.
- **Phase 3**: visual designer for JSON/YAML definitions. C# workflows stay view-only.
