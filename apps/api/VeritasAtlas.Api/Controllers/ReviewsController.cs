using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Reviews;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Application.Models;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/reviews")]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet]
    public async Task<ActionResult<GetReviewsResponse>> GetReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] Guid? caseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _reviewService.GetReviewsAsync(
            new ReviewListFilters
            {
                Page = page,
                PageSize = pageSize,
                Status = status,
                CaseId = caseId
            },
            cancellationToken);

        return Ok(new GetReviewsResponse(
            result.Items.Select(MapItem).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    [HttpPost]
    public async Task<ActionResult<SubmitReviewResponse>> SubmitReview(
        [FromBody] SubmitReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _reviewService.SubmitForReviewAsync(
            request.CaseId,
            request.SubmittedBy,
            request.Notes,
            cancellationToken);

        return Ok(new SubmitReviewResponse(
            entity.Id,
            entity.CaseId,
            entity.Status.ToString(),
            entity.Decision.ToString(),
            entity.Reviewer,
            entity.CreatedAtUtc));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<CompleteReviewResponse>> CompleteReview(
        Guid id,
        [FromBody] CompleteReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ReviewDecision>(request.Decision, true, out var decision))
        {
            return BadRequest(new { message = "Invalid decision." });
        }

        var entity = await _reviewService.CompleteReviewAsync(
            id,
            decision,
            request.Reviewer,
            request.Notes,
            cancellationToken);

        return Ok(new CompleteReviewResponse(
            entity.Id,
            entity.CaseId,
            entity.Status.ToString(),
            entity.Decision.ToString(),
            entity.Reviewer,
            entity.ReviewedAtUtc,
            entity.UpdatedAtUtc));
    }

    private static GetReviewsItemResponse MapItem(VeritasAtlas.Domain.Entities.Review entity)
    {
        return new GetReviewsItemResponse(
            entity.Id,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Decision.ToString(),
            entity.Reviewer,
            entity.CreatedAtUtc,
            entity.ReviewedAtUtc);
    }
}
