namespace VeritasAtlas.Api.Contracts.Cases;

public sealed record GetCaseClaimResponse(
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

public sealed record GetCaseContradictionResponse(
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

public sealed record GetCaseReviewResponse(
    Guid Id,
    Guid CaseId,
    string Type,
    string Status,
    string Decision,
    string Reviewer,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ReviewedAtUtc);

public sealed record GetCasePublicationResponse(
    Guid Id,
    Guid CaseId,
    string Channel,
    string Status,
    string Title,
    string Slug,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc);

public sealed record GetCaseResponse(
    Guid Id,
    string Title,
    string? Summary,
    string Status,
    Guid? SubjectPersonId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyCollection<GetCaseClaimResponse> Claims,
    IReadOnlyCollection<GetCaseContradictionResponse> Contradictions,
    IReadOnlyCollection<GetCaseReviewResponse> Reviews,
    IReadOnlyCollection<GetCasePublicationResponse> Publications);

public sealed record GetCasesItemResponse(
    Guid Id,
    string Title,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetCasesResponse(
    IReadOnlyCollection<GetCasesItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CreateCaseRequest(
    string Title,
    string? Summary,
    Guid? SubjectPersonId,
    string? CreatedBy);

public sealed record CreateCaseResponse(
    Guid Id,
    string Title,
    string Status,
    DateTime CreatedAtUtc);

public sealed record AddClaimToCaseRequest(
    Guid ClaimId,
    string? CreatedBy);

public sealed record ApproveCaseRequest(
    string? ApprovedBy,
    string? Notes);

public sealed record RejectCaseRequest(
    string? RejectedBy,
    string? Notes);
