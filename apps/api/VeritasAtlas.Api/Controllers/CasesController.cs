using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Cases;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/cases")]
public sealed class CasesController : ControllerBase
{
    private readonly ICaseService _caseService;

    public CasesController(ICaseService caseService)
    {
        _caseService = caseService;
    }

    [HttpGet]
    public async Task<ActionResult<GetCasesResponse>> GetCases(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "CreatedAt",
        [FromQuery] string sortDirection = "desc",
        CancellationToken cancellationToken = default)
    {
        var result = await _caseService.GetCasesAsync(
            new CaseListFilters
            {
                Page = page,
                PageSize = pageSize,
                Status = status,
                Search = search,
                SortBy = sortBy,
                SortDirection = sortDirection
            },
            cancellationToken);

        return Ok(new GetCasesResponse(
            result.Items.Select(MapCaseListItem).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetCaseResponse>> GetCaseById(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _caseService.GetCaseByIdAsync(id, cancellationToken);

        return Ok(new GetCaseResponse(
            entity.Id,
            entity.Title,
            entity.Summary,
            entity.Status.ToString(),
            entity.SubjectPersonId,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.Claims.Select(MapClaim).ToArray(),
            entity.Contradictions.Select(MapContradiction).ToArray(),
            entity.Reviews.Select(MapReview).ToArray(),
            entity.Publications.Select(MapPublication).ToArray()));
    }

    [HttpPost]
    public async Task<ActionResult<CreateCaseResponse>> CreateCase(
        [FromBody] CreateCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _caseService.CreateCaseAsync(
            request.Title,
            request.Summary,
            request.SubjectPersonId,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateCaseResponse(
            entity.Id,
            entity.Title,
            entity.Status.ToString(),
            entity.CreatedAtUtc);

        return CreatedAtAction(nameof(GetCaseById), new { id = entity.Id }, response);
    }

    [HttpPost("{id:guid}/claims")]
    public async Task<IActionResult> AddClaimToCase(
        Guid id,
        [FromBody] AddClaimToCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        await _caseService.AddClaimToCaseAsync(id, request.ClaimId, request.CreatedBy, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> ApproveCase(
        Guid id,
        [FromBody] ApproveCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        await _caseService.ApproveCaseAsync(id, request.ApprovedBy, request.Notes, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> RejectCase(
        Guid id,
        [FromBody] RejectCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        await _caseService.RejectCaseAsync(id, request.RejectedBy, request.Notes, cancellationToken);
        return NoContent();
    }

    private static GetCasesItemResponse MapCaseListItem(VeritasAtlas.Domain.Entities.Case entity)
    {
        return new GetCasesItemResponse(
            entity.Id,
            entity.Title,
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
    }

    private static GetCaseClaimResponse MapClaim(VeritasAtlas.Domain.Entities.Claim entity)
    {
        return new GetCaseClaimResponse(
            entity.Id,
            entity.StatementId,
            entity.PersonId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.NormalizedText,
            entity.IsMaterial,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
    }

    private static GetCaseContradictionResponse MapContradiction(VeritasAtlas.Domain.Entities.Contradiction entity)
    {
        return new GetCaseContradictionResponse(
            entity.Id,
            entity.CaseId,
            entity.LeftClaimId,
            entity.RightClaimId,
            entity.Type.ToString(),
            entity.Severity.ToString(),
            entity.Status.ToString(),
            entity.Summary,
            entity.Rationale,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
    }

    private static GetCaseReviewResponse MapReview(VeritasAtlas.Domain.Entities.Review entity)
    {
        return new GetCaseReviewResponse(
            entity.Id,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Decision.ToString(),
            entity.Reviewer,
            entity.Notes,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.ReviewedAtUtc);
    }

    private static GetCasePublicationResponse MapPublication(VeritasAtlas.Domain.Entities.Publication entity)
    {
        return new GetCasePublicationResponse(
            entity.Id,
            entity.CaseId,
            entity.Channel.ToString(),
            entity.Status.ToString(),
            entity.Title,
            entity.Slug ?? string.Empty,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.PublishedAtUtc);
    }
}

