using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkflowCore.Dashboard.Designer;
using WorkflowCore.Dashboard.Journal;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Tests;

public class GreetStep : StepBody
{
    public string Name { get; set; } = string.Empty;
    public string Greeting { get; private set; } = string.Empty;

    public override ExecutionResult Run(IStepExecutionContext context)
    {
        Greeting = $"Hello {Name}";
        return ExecutionResult.Next();
    }
}

public class GreetData
{
    public string Person { get; set; } = "Ada";
    public string? Message { get; set; }
}

public sealed class DesignerTests : IDisposable
{
    private static readonly string Step = StepCatalog.DslTypeName(typeof(GreetStep));
    private static readonly string Data = StepCatalog.DslTypeName(typeof(GreetData));

    private readonly InMemoryJournal _journal = new(Options.Create(new DashboardOptions()));
    private readonly ServiceProvider _node;

    public DesignerTests() => _node = Node(_journal);

    /// <summary>One app instance. Several nodes share a journal the way they would share a database.</summary>
    private static ServiceProvider Node(IDashboardJournal journal)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddWorkflow();
        services.AddWorkflowCoreDashboard().UseJournal(_ => journal).AddDesigner();
        return services.BuildServiceProvider();
    }

    private static JsonObject Definition(params JsonObject[] steps) => new()
    {
        ["Id"] = "Greeting",
        ["DataType"] = Data,
        ["Steps"] = new JsonArray(steps),
    };

    private static JsonObject Greet(string id, string? next = null, string name = "data.Person") => new()
    {
        ["Id"] = id,
        ["StepType"] = Step,
        ["Inputs"] = new JsonObject { ["Name"] = name },
        ["Outputs"] = new JsonObject { ["Message"] = "step.Greeting" },
        ["NextStepId"] = next,
    };

    [Fact]
    public void Catalog_lists_primitives_and_app_steps_with_their_inputs()
    {
        var catalog = _node.GetRequiredService<StepCatalog>().Get();

        var parallel = catalog.Steps.Single(s => s.Name == "Parallel");
        Assert.True(parallel.IsContainer);
        Assert.True(parallel.MultipleBranches);

        var greet = catalog.Steps.Single(s => s.Type == Step);
        Assert.Equal(["Name"], greet.Inputs.Select(i => i.Name));
        Assert.Contains(greet.Outputs, o => o.Name == "Greeting" && o.Kind == "string");

        Assert.Contains(catalog.DataTypes, d => d.Type == Data && d.Properties.Any(p => p.Name == "Person"));
    }

    [Fact]
    public void A_correct_definition_is_valid()
    {
        var result = _node.GetRequiredService<DefinitionValidator>().Validate(Definition(Greet("a", "b"), Greet("b")));
        Assert.True(result.Valid, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Structural_problems_are_all_reported_with_their_step()
    {
        var unknownType = Greet("c");
        unknownType["StepType"] = "Nope.Missing, Nope";
        var badInput = Greet("d");
        ((JsonObject)badInput["Inputs"]!)["Colour"] = "\"red\"";

        var result = _node.GetRequiredService<DefinitionValidator>().Validate(
            Definition(Greet("a", "missing"), Greet("b"), unknownType, badInput));

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, i => i.StepId == "a" && i.Message.Contains("'missing'"));
        Assert.Contains(result.Issues, i => i.StepId == "c" && i.Severity == "error");
        Assert.Contains(result.Issues, i => i.StepId == "d" && i.Message.Contains("Colour"));
        Assert.Contains(result.Issues, i => i.StepId == "b" && i.Severity == "warning");
    }

    [Fact]
    public void Expression_errors_come_from_workflow_cores_own_loader()
    {
        var result = _node.GetRequiredService<DefinitionValidator>().Validate(Definition(Greet("a", name: "data.Nobody")));

        Assert.False(result.Valid);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("Nobody", issue.Message);
        Assert.Equal("a", issue.StepId);
        // Validation must not register anything.
        Assert.False(_node.GetRequiredService<IWorkflowRegistry>().IsRegistered("Greeting", 0));
    }

    [Fact]
    public void Every_step_with_a_bad_expression_is_reported()
    {
        var result = _node.GetRequiredService<DefinitionValidator>().Validate(
            Definition(Greet("a", "b", name: "data.Nobody"), Greet("b", "c"), Greet("c", name: "data.Nothing")));

        Assert.Equal(["a", "c"], result.Issues.Select(i => i.StepId));
    }

    [Fact]
    public async Task Publishing_registers_increasing_versions_and_clears_the_draft()
    {
        var designer = _node.GetRequiredService<DesignerService>();
        var registry = _node.GetRequiredService<IWorkflowRegistry>();

        await designer.SaveDraft("Greeting", new SaveDesignRequest(Definition(Greet("a")), null), default);
        var first = await designer.Publish("Greeting", new SaveDesignRequest(Definition(Greet("a")), null), default);
        var second = await designer.Publish("Greeting", new SaveDesignRequest(Definition(Greet("a", "b"), Greet("b")), null), default);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.True(registry.IsRegistered("Greeting", 2));
        Assert.Equal(2, registry.GetDefinition("Greeting")!.Steps.Count);

        var document = await designer.Get("Greeting", default);
        Assert.Null(document!.Draft);
        Assert.Equal([1, 2], document.Versions.Select(v => v.Version));
    }

    [Fact]
    public async Task An_invalid_definition_is_not_published()
    {
        var designer = _node.GetRequiredService<DesignerService>();
        var result = await designer.Publish("Greeting", new SaveDesignRequest(Definition(Greet("a", "nowhere")), null), default);

        Assert.False(result.Validation.Valid);
        Assert.False(_node.GetRequiredService<IWorkflowRegistry>().IsRegistered("Greeting", 1));
        Assert.Null(await designer.Get("Greeting", default));
    }

    [Fact]
    public async Task Another_node_registers_versions_published_elsewhere()
    {
        await _node.GetRequiredService<DesignerService>()
            .Publish("Greeting", new SaveDesignRequest(Definition(Greet("a")), null), default);

        using var other = Node(_journal);
        var added = await other.GetRequiredService<DesignerService>().RegisterStoredVersions(default);

        Assert.Equal(1, added);
        Assert.True(other.GetRequiredService<IWorkflowRegistry>().IsRegistered("Greeting", 1));
        Assert.Equal(0, await other.GetRequiredService<DesignerService>().RegisterStoredVersions(default));
    }

    [Fact]
    public void Yaml_imports_as_canonical_json()
    {
        var yaml = $"""
            Id: FromYaml
            Version: 1
            DataType: {Data}
            Steps:
              - Id: hello
                StepType: {Step}
                Inputs:
                  Name: '"Grace"'
            """;

        var source = DesignerService.Import(yaml).Source;

        Assert.Equal("FromYaml", source["Id"]!.GetValue<string>());
        var step = source["Steps"]![0]!;
        Assert.Equal("hello", step["Id"]!.GetValue<string>());
        Assert.Equal("\"Grace\"", step["Inputs"]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Invalid_ids_are_rejected()
    {
        var designer = _node.GetRequiredService<DesignerService>();
        await Assert.ThrowsAsync<DesignerException>(() =>
            designer.SaveDraft("not valid!", new SaveDesignRequest(Definition(Greet("a")), null), default));
    }

    public void Dispose() => _node.Dispose();
}
