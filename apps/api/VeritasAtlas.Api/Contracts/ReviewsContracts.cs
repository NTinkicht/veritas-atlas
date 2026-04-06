namespace VeritasAtlas.Api.Contracts.Reviews;

public sealed record SubmitReviewRequest(
    Guid CaseId,
    string? SubmittedBy,
    string? Notes);

public sealed record SubmitReviewResponse(
    Guid Id,
    Guid CaseId,
    string Status,
    string Decision,
    string Reviewer,
    DateTime CreatedAtUtc);

public sealed record CompleteReviewRequest(
    string Decision,
    string? Reviewer,
    string? Notes);

public sealed record CompleteReviewResponse(
    Guid Id,
    Guid CaseId,
    string Status,
    string Decision,
    string Reviewer,
    DateTime? ReviewedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetReviewsItemResponse(
    Guid Id,
    Guid CaseId,
    string Type,
    string Status,
    string Decision,
    string Reviewer,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc);

public sealed record GetReviewsResponse(
    IReadOnlyCollection<GetReviewsItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
