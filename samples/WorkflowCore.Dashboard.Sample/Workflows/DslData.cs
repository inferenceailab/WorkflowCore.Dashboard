namespace WorkflowCore.Dashboard.Sample.Workflows;

/// <summary>Data type of the JSON "ExpenseApproval" definition.</summary>
public class ApprovalData
{
    public string RequestId { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = "alex";
    public decimal Amount { get; set; } = 250;
}

/// <summary>Data type of the YAML "EmployeeOnboarding" definition.</summary>
public class OnboardingData
{
    public string EmployeeName { get; set; } = "Sam Rivera";
    public int LaptopDelaySeconds { get; set; } = 8;
}
