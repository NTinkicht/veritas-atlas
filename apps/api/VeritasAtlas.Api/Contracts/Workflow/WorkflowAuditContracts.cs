namespace VeritasAtlas.Api.Contracts.Workflow;

public sealed record WorkflowAuditEntryResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string ActionName,
    string? PreviousStatus,
    string NextStatus,
    string Role,
    bool Success,
    string Message,
    DateTime TimestampUtc);

public sealed record WorkflowValidationFailureResponse(
    string EntityType,
    Guid EntityId,
    string RequestedAction,
    string Message,
    DateTime TimestampUtc);