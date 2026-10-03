using WorkflowCore.Dashboard;
using WorkflowCore.Dashboard.Sample;
using WorkflowCore.Dashboard.Sample.Steps;

var builder = WebApplication.CreateBuilder(args);

var database = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Sample:Database"] ?? "workflows.db");

builder.Services.AddWorkflow(options => options.UseSqlite($"Data Source={database};", canCreateDB: true));
builder.Services.AddWorkflowDSL();
builder.Services.AddTransient<LogStep>();

builder.Services.AddWorkflowCoreDashboard(options =>
{
    options.Title = "Workflow Core Sample";
});

builder.Services.AddHostedService<WorkflowStartup>();
if (builder.Configuration.GetValue("Sample:GenerateTraffic", true))
    builder.Services.AddHostedService<TrafficGenerator>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/workflows"));
app.MapWorkflowCoreDashboard("/workflows");

app.Run();
