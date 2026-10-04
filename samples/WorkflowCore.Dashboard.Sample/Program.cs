using Microsoft.EntityFrameworkCore;
using WorkflowCore.Dashboard;
using WorkflowCore.Dashboard.Designer;
using WorkflowCore.Dashboard.EntityFramework;
using WorkflowCore.Dashboard.OpenIdConnect;
using WorkflowCore.Dashboard.Sample;
using WorkflowCore.Dashboard.Sample.Steps;

var builder = WebApplication.CreateBuilder(args);

var database = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Sample:Database"] ?? "workflows.db");

builder.Services.AddWorkflow(options => options.UseSqlite($"Data Source={database};", canCreateDB: true));
builder.Services.AddWorkflowDSL();
builder.Services.AddTransient<LogStep>();

var dashboard = builder.Services
    .AddWorkflowCoreDashboard(options =>
    {
        options.Title = "Workflow Core Sample";
    })
    // Activity history and the instance index live next to Workflow Core's tables and survive restarts.
    .UseEntityFrameworkJournal(db => db.UseSqlite($"Data Source={database};"))
    // Visual editor for JSON/YAML workflows; published designs are stored in the journal database.
    .AddDesigner();

// Sign-in with Okta, Entra ID, Auth0, Keycloak, Google, Cognito or any OpenID Connect provider, when configured.
// Without it, the dashboard is only reachable from this machine.
var signIn = builder.Configuration.GetSection("Dashboard:SignIn");
if (signIn.Exists())
    dashboard.UseSignIn(signIn);

builder.Services.AddHostedService<WorkflowStartup>();
if (builder.Configuration.GetValue("Sample:GenerateTraffic", true))
    builder.Services.AddHostedService<TrafficGenerator>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/workflows"));
app.MapWorkflowCoreDashboard("/workflows");

app.Run();
