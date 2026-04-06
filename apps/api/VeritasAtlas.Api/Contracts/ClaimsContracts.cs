namespace VeritasAtlas.Api.Contracts.Claims;

public sealed record CreateClaimRequest(
    Guid StatementId,
    string Text,
    Guid? PersonId,
    string? CreatedBy);

public sealed record CreateClaimResponse(
    Guid Id,
    Guid StatementId,
    Guid? PersonId,
    string Type,
    string Status,
    string? Topic,
    string? NormalizedText,
    bool IsMaterial,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
