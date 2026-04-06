namespace VeritasAtlas.Api.Contracts.Contradictions;

public sealed record CreateContradictionRequest(
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    string Topic,
    string Summary,
    string? ContradictionType,
    string? Severity,
    Guid? CaseId);

public sealed record CreateContradictionResponse(
    Guid Id,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid? CaseId,
    string Topic,
    string Summary,
    string ContradictionType,
    string Severity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetContradictionsItemResponse(
    Guid Id,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid? CaseId,
    string Topic,
    string Summary,
    string ContradictionType,
    string Severity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetContradictionResponse(
    Guid Id,
    Guid PrimaryClaimId,
    Guid SecondaryClaimId,
    Guid? CaseId,
    string Topic,
    string Summary,
    string ContradictionType,
    string Severity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetContradictionsResponse(
    IReadOnlyCollection<GetContradictionsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);