# Getting started

## Requirements

- An ASP.NET Core app on .NET 8, 9 or 10 that uses Workflow Core 3.x.
- Any Workflow Core persistence provider. Some provider features differ; see [Activity journal](Activity-Journal.md#instance-listing).

## 1. Install the packages

The packages are attached to each [GitHub release](https://github.com/inferenceailab/WorkflowCore.Dashboard/releases). Download them into a local feed and add them like any other package:

```bash
mkdir packages
gh release download v0.1.0 -R inferenceailab/WorkflowCore.Dashboard -p "*.nupkg" -D packages
dotnet nuget add source "$(pwd)/packages" --name wfc-dashboard

dotnet add package WorkflowCore.Dashboard --version 0.1.0
dotnet add package WorkflowCore.Dashboard.EntityFramework --version 0.1.0   # optional: keep history
dotnet add package WorkflowCore.Dashboard.Designer --version 0.1.0          # optional: visual designer
```

## 2. Add the dashboard

```csharp
using WorkflowCore.Dashboard;

builder.Services.AddWorkflow(options => options.UseSqlServer(connectionString, canCreateDB: true));
builder.Services.AddWorkflowCoreDashboard(options =>
{
    options.Title = "Orders";
});

var app = builder.Build();
app.MapWorkflowCoreDashboard("/workflows");
app.Run();
```

Start the app and open `https://localhost:<port>/workflows` **on the same machine**. By default the dashboard only answers local requests; see step 5 to open it up.

`AddWorkflowCoreDashboard` must come after `AddWorkflow`. The dashboard does not start the Workflow Core host; your app does, as before.

## 3. Keep history across restarts (recommended)

Without a persistent journal, the activity feed and totals are kept in memory and the instance list uses Workflow Core's own listing. Store them in a database instead:

```csharp
using Microsoft.EntityFrameworkCore;
using WorkflowCore.Dashboard.EntityFramework;

builder.Services.AddWorkflowCoreDashboard()
    .UseEntityFrameworkJournal(db => db.UseSqlServer(connectionString));
```

It works with any EF Core relational provider and can share Workflow Core's database. See [Activity journal](Activity-Journal.md).

## 4. Add the designer (optional)

```csharp
using WorkflowCore.Dashboard.Designer;

builder.Services.AddWorkflowCoreDashboard()
    .UseEntityFrameworkJournal(db => db.UseSqlServer(connectionString))
    .AddDesigner();
```

A **Designer** item appears in the navigation. See [Designer](Designer.md).

## 5. Allow access from other machines

Put the dashboard behind your app's authentication before exposing it:

```csharp
builder.Services.AddWorkflowCoreDashboard(options =>
{
    // The authorization policy below decides who gets in.
    options.Authorization = _ => true;
});

app.MapWorkflowCoreDashboard("/workflows").RequireAuthorization("WorkflowAdmins");
```

Read [Security](Security.md) first: anyone who can open the dashboard can see workflow data and, unless it is read-only, change workflows.

## Try the sample

```bash
git clone https://github.com/inferenceailab/WorkflowCore.Dashboard.git
cd WorkflowCore.Dashboard
dotnet run --project samples/WorkflowCore.Dashboard.Sample
```

Open http://localhost:5290/workflows. The sample uses SQLite for Workflow Core and the journal, has the designer enabled, and runs C#, JSON and YAML workflows. It starts a new workflow every few seconds; set `Sample:GenerateTraffic` to `false` in `appsettings.json` to stop that.
