namespace VeritasAtlas.Api.Contracts.Claims;

public sealed record CreateClaimRequest(
    Guid StatementId,
    string Topic,
    string NormalizedText,
    string? Type,
    Guid? PersonId,
    Guid? CaseId,
    bool IsMaterial);

public sealed record CreateClaimResponse(
    Guid Id,
    Guid StatementId,
    Guid? PersonId,
    Guid? CaseId,
    string Type,
    string Status,
    string Topic,
    string NormalizedText,
    bool IsMaterial,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetClaimsItemResponse(
    Guid Id,
    Guid StatementId,
    Guid? PersonId,
    Guid? CaseId,
    string Type,
    string Status,
    string Topic,
    string NormalizedText,
    bool IsMaterial,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetClaimResponse(
    Guid Id,
    Guid StatementId,
    Guid? PersonId,
    Guid? CaseId,
    string Type,
    string Status,
    string Topic,
    string NormalizedText,
    bool IsMaterial,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetClaimsResponse(
    IReadOnlyCollection<GetClaimsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
