using WorkflowCore.Dashboard.Sample.Steps;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace WorkflowCore.Dashboard.Sample.Workflows;

public class OrderData
{
    public string OrderId { get; set; } = string.Empty;
    public string Customer { get; set; } = "Contoso";
    public decimal Amount { get; set; } = 120;
    public bool Express { get; set; }
    public int PaymentFailures { get; set; }
    public object? Shipment { get; set; }
}

/// <summary>
/// Order fulfilment: retries a flaky payment step, branches on express shipping,
/// then waits for an external "OrderShipped" event keyed by the order ID.
/// </summary>
public class OrderWorkflow : IWorkflow<OrderData>
{
    public string Id => "OrderProcessing";
    public int Version => 1;

    public void Build(IWorkflowBuilder<OrderData> builder)
    {
        builder
            .StartWith<LogStep>()
                .Name("Receive order")
                .Input(step => step.Message, data => "Order " + data.OrderId + " received")
            .Then<WorkStep>()
                .Name("Validate order")
            .Then<ChargePayment>()
                .Name("Charge payment")
                .Input(step => step.FailuresBeforeSuccess, data => data.PaymentFailures)
                .OnError(WorkflowErrorHandling.Retry, TimeSpan.FromSeconds(5))
            .If(data => data.Express)
                .Do(then => then
                    .StartWith<WorkStep>()
                        .Name("Prioritize shipping")
                        .Input(step => step.Milliseconds, data => 400))
            .Then<WorkStep>()
                .Name("Pack items")
            .WaitFor("OrderShipped", data => data.OrderId)
                .Name("Wait for shipment")
                .Output(data => data.Shipment, step => step.EventData)
            .Then<LogStep>()
                .Name("Send confirmation")
                .Input(step => step.Message, data => "Order " + data.OrderId + " shipped");
    }
}

/// <summary>Version 2 adds an inventory reservation that suspends the workflow when it fails.</summary>
public class OrderWorkflowV2 : IWorkflow<OrderData>
{
    public string Id => "OrderProcessing";
    public int Version => 2;

    public void Build(IWorkflowBuilder<OrderData> builder)
    {
        builder
            .StartWith<LogStep>()
                .Name("Receive order")
                .Input(step => step.Message, data => "Order " + data.OrderId + " received")
            .Then<ReserveInventory>()
                .Name("Reserve inventory")
                .OnError(WorkflowErrorHandling.Suspend)
            .Then<ChargePayment>()
                .Name("Charge payment")
                .Input(step => step.FailuresBeforeSuccess, data => data.PaymentFailures)
                .OnError(WorkflowErrorHandling.Retry, TimeSpan.FromSeconds(5))
            .WaitFor("OrderShipped", data => data.OrderId)
                .Name("Wait for shipment")
                .Output(data => data.Shipment, step => step.EventData)
            .Then<LogStep>()
                .Name("Send confirmation")
                .Input(step => step.Message, data => "Order " + data.OrderId + " shipped");
    }
}

public class BatchData
{
    public List<string> Files { get; set; } = ["customers.csv", "orders.csv", "invoices.csv"];
    public int DelaySeconds { get; set; } = 10;
}

/// <summary>Parallel ForEach over files, then a timed delay: shows container steps and sleeping pointers.</summary>
public class BatchImportWorkflow : IWorkflow<BatchData>
{
    public string Id => "BatchImport";
    public int Version => 1;

    public void Build(IWorkflowBuilder<BatchData> builder)
    {
        builder
            .StartWith<LogStep>()
                .Name("Prepare import")
                .Input(step => step.Message, data => "Importing " + data.Files.Count + " files")
            .ForEach(data => data.Files)
                .Do(each => each
                    .StartWith<WorkStep>()
                        .Name("Import file")
                        .Input(step => step.Milliseconds, data => 1500))
            .Delay(data => TimeSpan.FromSeconds(data.DelaySeconds))
                .Name("Cool-down")
            .Then<LogStep>()
                .Name("Publish report")
                .Input(step => step.Message, data => "Import finished");
    }
}
