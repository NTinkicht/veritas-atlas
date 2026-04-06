using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Contradictions;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/contradictions")]
public sealed class ContradictionsController : ControllerBase
{
    private readonly ContradictionSliceService _contradictionSliceService;

    public ContradictionsController(ContradictionSliceService contradictionSliceService)
    {
        _contradictionSliceService = contradictionSliceService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateContradictionResponse>> CreateContradiction(
        [FromBody] CreateContradictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _contradictionSliceService.CreateContradictionAsync(
            request.PrimaryClaimId,
            request.SecondaryClaimId,
            request.Topic,
            request.Summary,
            request.ContradictionType,
            request.Severity,
            request.CaseId,
            cancellationToken);

        var response = new CreateContradictionResponse(
            entity.Id,
            entity.LeftClaimId,
            entity.RightClaimId,
            entity.CaseId,
            request.Topic,
            entity.Summary,
            entity.Type.ToString(),
            entity.Severity.ToString(),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<GetContradictionsResponse>> GetContradictions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? claimId = null,
        [FromQuery] Guid? caseId = null,
        CancellationToken cancellationToken = default)
    {
        var (total, items) = await _contradictionSliceService.GetContradictionsAsync(page, pageSize, claimId, caseId, cancellationToken);

        var mapped = items.Select(entity => new GetContradictionsItemResponse(
            entity.Id,
            entity.LeftClaimId,
            entity.RightClaimId,
            entity.CaseId,
            entity.Summary,
            entity.Summary,
            entity.Type.ToString(),
            entity.Severity.ToString(),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        )).ToArray();

        return Ok(new GetContradictionsResponse(
            mapped,
            page,
            pageSize,
            total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        ));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetContradictionResponse>> GetContradiction(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _contradictionSliceService.GetContradictionByIdAsync(id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetContradictionResponse(
            entity.Id,
            entity.LeftClaimId,
            entity.RightClaimId,
            entity.CaseId,
            entity.Summary,
            entity.Summary,
            entity.Type.ToString(),
            entity.Severity.ToString(),
            entity.Status.ToString(),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        ));
    }
}