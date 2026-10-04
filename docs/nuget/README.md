# Workflow Core Dashboard

An embeddable dashboard for [Workflow Core](https://github.com/danielgerlag/workflow-core): monitor running workflows, inspect instances as flowcharts, control them, and design JSON/YAML workflows in the browser.

```csharp
builder.Services.AddWorkflow();
builder.Services.AddWorkflowCoreDashboard();

var app = builder.Build();
app.MapWorkflowCoreDashboard("/workflows");
```

| Package | Adds |
|---|---|
| `WorkflowCore.Dashboard` | The UI, its API and live updates; activity kept in memory |
| `WorkflowCore.Dashboard.EntityFramework` | Activity history and instance index in any EF Core relational database |
| `WorkflowCore.Dashboard.Designer` | The visual designer for JSON/YAML workflows |

By default the dashboard only answers requests from the same machine. Read the [security guide](https://github.com/inferenceailab/WorkflowCore.Dashboard/blob/main/docs/Security.md) before exposing it.

Documentation, screenshots and the sample app: https://github.com/inferenceailab/WorkflowCore.Dashboard
