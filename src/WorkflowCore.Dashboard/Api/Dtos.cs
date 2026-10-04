using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkflowCore.Dashboard.Api;

public sealed record DashboardConfig(
    string Title,
    bool AllowActions,
    string PersistenceProvider,
    DateTime StartedAt,
    JournalInfo Journal,
    IReadOnlyList<string> Features,
    DashboardUser? User,
    string? SignOutPath);

/// <summary>The signed-in user, as shown in the UI's account menu.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Email">Email address, when the identity provider sends one.</param>
/// <param name="Role"><c>admin</c>, <c>viewer</c>, or <c>null</c> when the app does not assign dashboard roles.</param>
public sealed record DashboardUser(string Name, string? Email, string? Role);

public sealed record JournalInfo(string Name, bool Persistent, int? RetentionDays, bool StepEvents);

public sealed record DefinitionSummary(
    string Id,
    int Version,
    string? Description,
    string? DataType,
    int StepCount,
    bool IsLatest);

public sealed record DefinitionDetail(
    string Id,
    int Version,
    string? Description,
    string? DataType,
    string DefaultErrorBehavior,
    string? DefaultErrorRetryInterval,
    JsonNode? DataTemplate,
    IReadOnlyList<StepDto> Steps);

public sealed record StepDto(
    int Id,
    string? ExternalId,
    string Name,
    string StepType,
    string StepTypeFullName,
    IReadOnlyList<int> Children,
    IReadOnlyList<OutcomeDto> Outcomes,
    string? ErrorBehavior,
    string? RetryInterval,
    int? CompensationStepId);

public sealed record OutcomeDto(int NextStep, string? Label, string? ExternalNextStepId);

public sealed record InstanceSummary(
    string Id,
    string DefinitionId,
    int Version,
    string? Description,
    string? Reference,
    string Status,
    DateTime CreateTime,
    DateTime? CompleteTime,
    DateTime? NextExecution,
    int ActivePointers,
    int FailedPointers,
    string? CurrentStep);

public sealed record InstanceDetail(
    InstanceSummary Summary,
    JsonNode? Data,
    IReadOnlyList<PointerDto> ExecutionPointers);

public sealed record PointerDto(
    string Id,
    int StepId,
    string StepName,
    string? StepType,
    string Status,
    bool Active,
    DateTime? StartTime,
    DateTime? EndTime,
    DateTime? SleepUntil,
    int RetryCount,
    string? EventName,
    string? EventKey,
    bool EventPublished,
    string? PredecessorId,
    IReadOnlyList<string> Children,
    IReadOnlyList<string> Scope,
    JsonNode? Outcome,
    JsonNode? EventData,
    JsonNode? PersistenceData,
    JsonNode? ContextItem);

/// <param name="Total">Number of matches, when the source can count them.</param>
/// <param name="Source">"journal" (newest first, with totals) or "provider" (storage order).</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Skip, int Take, bool HasMore, long? Total, string Source);

public sealed record StartWorkflowRequest(string DefinitionId, int? Version, JsonElement? Data, string? Reference);

public sealed record StartWorkflowResponse(string Id);

public sealed record PublishEventRequest(string EventName, string EventKey, JsonElement? EventData, DateTime? EffectiveDate);

public sealed record ActionResponse(bool Success);

public sealed record ApiError(string Code, string Message);
