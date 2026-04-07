namespace VeritasAtlas.Api.Contracts.Persistence;

public sealed record ScenarioSnapshotResponse(
    bool Exists,
    Guid? CaseId,
    Guid? ClaimAId,
    Guid? ClaimBId,
    Guid? ContradictionId,
    DateTime? CreatedAtUtc,
    string? CaseStatus,
    string? ContradictionStatus,
    DateTime TimestampUtc);

public sealed record ResetResponse(
    bool Success,
    string Message,
    DateTime TimestampUtc);