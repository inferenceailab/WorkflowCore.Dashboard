using WorkflowCore.Dashboard.Sample.Workflows;
using WorkflowCore.Interface;

namespace WorkflowCore.Dashboard.Sample;

/// <summary>
/// Starts a mix of workflows every few seconds and later publishes the events they wait for,
/// so the dashboard has live activity to show. Disable with Sample:GenerateTraffic=false.
/// </summary>
public sealed class TrafficGenerator(IWorkflowController workflows, ILogger<TrafficGenerator> logger) : BackgroundService
{
    private readonly Random _random = new();
    private int _counter = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the workflow host time to start.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartSomething(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Traffic generator failed to start a workflow");
            }

            await Task.Delay(TimeSpan.FromSeconds(_random.Next(8, 16)), stoppingToken);
        }
    }

    private async Task StartSomething(CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _counter);
        switch (_random.Next(10))
        {
            case < 4:
            {
                var order = new OrderData
                {
                    OrderId = $"ORD-{n}",
                    Customer = Pick("Contoso", "Fabrikam", "Northwind", "Tailspin"),
                    Amount = _random.Next(20, 900),
                    Express = _random.Next(3) == 0,
                    PaymentFailures = _random.Next(4) == 0 ? 2 : 0,
                };
                var version = _random.Next(4) == 0 ? 2 : 1;
                await workflows.StartWorkflow("OrderProcessing", version, order, order.OrderId);
                PublishLater("OrderShipped", order.OrderId, new { carrier = "DHL", trackingNumber = $"1Z{n}" }, ct);
                break;
            }
            case < 6:
            {
                var request = new ApprovalData { RequestId = $"EXP-{n}", Amount = _random.Next(50, 3000), RequestedBy = Pick("alex", "kim", "jo") };
                await workflows.StartWorkflow("ExpenseApproval", null, request, request.RequestId);
                // Leave some approvals waiting so there is something to publish from the dashboard.
                if (_random.Next(3) > 0)
                    PublishLater("ApprovalDecision", request.RequestId, "approved", ct);
                break;
            }
            case < 8:
                await workflows.StartWorkflow("EmployeeOnboarding", null, new OnboardingData { EmployeeName = Pick("Sam Rivera", "Lee Chen", "Ana Costa") }, $"EMP-{n}");
                break;
            default:
                await workflows.StartWorkflow("BatchImport", null, new BatchData(), $"BATCH-{n}");
                break;
        }
    }

    private void PublishLater(string eventName, string eventKey, object data, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(_random.Next(20, 60));
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay, ct);
            await workflows.PublishEvent(eventName, eventKey, data);
        }, ct);
    }

    private string Pick(params string[] values) => values[_random.Next(values.Length)];
}
