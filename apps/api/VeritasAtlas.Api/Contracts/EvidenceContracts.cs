namespace VeritasAtlas.Api.Contracts.Evidence;

public sealed record CreateEvidenceRequest(
    Guid DocumentId,
    string Quote,
    int? StartOffset,
    int? EndOffset,
    string? Context,
    string? CreatedBy);

public sealed record CreateEvidenceResponse(
    Guid Id,
    Guid? DocumentId,
    string Quote,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetEvidenceItemResponse(
    Guid Id,
    Guid? DocumentId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetEvidenceResponse(
    IReadOnlyCollection<GetEvidenceItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
