namespace VeritasAtlas.Api.Contracts.Contradictions;

public sealed record CreateContradictionRequest(
    Guid CaseId,
    Guid LeftClaimId,
    Guid RightClaimId,
    string Type,
    string Summary,
    string? Rationale,
    string? CreatedBy);

public sealed record CreateContradictionResponse(
    Guid Id,
    Guid CaseId,
    Guid LeftClaimId,
    Guid RightClaimId,
    string Type,
    string Severity,
    string Status,
    string Summary,
    string? Rationale,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
