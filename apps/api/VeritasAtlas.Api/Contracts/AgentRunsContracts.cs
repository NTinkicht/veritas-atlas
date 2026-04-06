namespace VeritasAtlas.Api.Contracts.AgentRuns;

public sealed record StartAgentRunRequest(
    string AgentName,
    string AgentType,
    Guid? CaseId,
    string? InputPayload,
    string? CreatedBy);

public sealed record StartAgentRunResponse(
    Guid Id,
    Guid? CaseId,
    string AgentName,
    string AgentType,
    string Status,
    DateTime StartedAtUtc,
    DateTime CreatedAtUtc);

public sealed record CompleteAgentRunRequest(
    string? OutputPayload,
    string? ErrorMessage,
    string? Notes,
    string? UpdatedBy);

public sealed record FailAgentRunRequest(
    string? ErrorMessage,
    string? Notes,
    string? UpdatedBy);

public sealed record GetAgentRunsItemResponse(
    Guid Id,
    Guid? CaseId,
    string AgentName,
    string AgentType,
    string Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc);

public sealed record GetAgentRunsResponse(
    IReadOnlyCollection<GetAgentRunsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
