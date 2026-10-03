using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Sample.Steps;

/// <summary>Writes a message to the log. Used by both the C# and the JSON/YAML workflows.</summary>
public class LogStep : StepBody
{
    private readonly ILogger<LogStep> _logger;

    public LogStep(ILogger<LogStep> logger) => _logger = logger;

    public string Message { get; set; } = string.Empty;

    public override ExecutionResult Run(IStepExecutionContext context)
    {
        _logger.LogInformation("[{WorkflowId}] {Message}", context.Workflow.Id, Message);
        return ExecutionResult.Next();
    }
}

/// <summary>Simulates slow work so steps show up as running on the dashboard.</summary>
public class WorkStep : StepBodyAsync
{
    public int Milliseconds { get; set; } = 800;

    public override async Task<ExecutionResult> RunAsync(IStepExecutionContext context)
    {
        await Task.Delay(Milliseconds);
        return ExecutionResult.Next();
    }
}

/// <summary>Fails on the first attempts, to show retries and step errors on the dashboard.</summary>
public class ChargePayment : StepBody
{
    public int FailuresBeforeSuccess { get; set; }

    public override ExecutionResult Run(IStepExecutionContext context)
    {
        // RetryCount is incremented by Workflow Core each time the Retry error behavior re-runs the step.
        var attempt = context.ExecutionPointer.RetryCount;
        if (attempt < FailuresBeforeSuccess)
            throw new TimeoutException($"Payment gateway timed out (attempt {attempt + 1}).");
        return ExecutionResult.Next();
    }
}

/// <summary>
/// Fails the first time for each workflow. Paired with the Suspend error behavior, it shows
/// a suspended workflow that succeeds after "Resume" on the dashboard.
/// </summary>
public class ReserveInventory : StepBody
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Attempts = new();

    public override ExecutionResult Run(IStepExecutionContext context)
    {
        if (Attempts.AddOrUpdate(context.Workflow.Id, 1, (_, n) => n + 1) == 1)
            throw new InvalidOperationException("Warehouse service unavailable. Resume the workflow to try again.");
        return ExecutionResult.Next();
    }
}
