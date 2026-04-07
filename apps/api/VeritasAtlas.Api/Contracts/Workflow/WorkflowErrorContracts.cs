namespace VeritasAtlas.Api.Contracts.Workflow;

public sealed record WorkflowErrorResponse(
    string Code,
    string Message,
    string? Detail,
    string Path,
    DateTime TimestampUtc);