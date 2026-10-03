using WorkflowCore.Dashboard.Sample.Workflows;
using WorkflowCore.Interface;
using WorkflowCore.Services.DefinitionStorage;

namespace WorkflowCore.Dashboard.Sample;

/// <summary>Registers the C# workflows, loads the JSON/YAML definitions, then starts the Workflow Core host.</summary>
public sealed class WorkflowStartup(IWorkflowHost host, IDefinitionLoader loader, ILogger<WorkflowStartup> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        host.RegisterWorkflow<OrderWorkflow, OrderData>();
        host.RegisterWorkflow<OrderWorkflowV2, OrderData>();
        host.RegisterWorkflow<BatchImportWorkflow, BatchData>();

        var folder = Path.Combine(AppContext.BaseDirectory, "Definitions");
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var deserializer = Path.GetExtension(file) switch
            {
                ".json" => Deserializers.Json,
                ".yaml" or ".yml" => Deserializers.Yaml,
                _ => null,
            };
            if (deserializer is null)
                continue;

            var def = loader.LoadDefinition(await File.ReadAllTextAsync(file, cancellationToken), deserializer);
            logger.LogInformation("Loaded {Definition} v{Version} from {File}", def.Id, def.Version, Path.GetFileName(file));
        }

        await host.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => host.StopAsync(cancellationToken);
}
