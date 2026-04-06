namespace VeritasAtlas.Api.Contracts.Sources;

public sealed record SourceReferenceResponse(
    string? ExternalId,
    string? Url,
    string? Domain,
    string? LanguageCode);

public sealed record CreateSourceRequest(
    string Name,
    string Type,
    string? Reference,
    string? CreatedBy);

public sealed record CreateSourceResponse(
    Guid Id,
    string Name,
    string Type,
    SourceReferenceResponse? Reference,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetSourcesItemResponse(
    Guid Id,
    string Name,
    string Type,
    SourceReferenceResponse? Reference,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetSourcesResponse(
    IReadOnlyCollection<GetSourcesItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
