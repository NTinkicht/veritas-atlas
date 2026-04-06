namespace VeritasAtlas.Api.Contracts.Documents;

public sealed record CreateDocumentRequest(
    Guid SourceId,
    string Title,
    string Content,
    string? ExternalReference,
    string? CreatedBy);

public sealed record CreateDocumentResponse(
    Guid Id,
    Guid SourceId,
    string Title,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetDocumentsItemResponse(
    Guid Id,
    Guid SourceId,
    string Title,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetDocumentsResponse(
    IReadOnlyCollection<GetDocumentsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
