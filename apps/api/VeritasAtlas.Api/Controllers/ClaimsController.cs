using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Claims;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/claims")]
public sealed class ClaimsController : ControllerBase
{
    private readonly ClaimSliceService _claimSliceService;

    public ClaimsController(ClaimSliceService claimSliceService)
    {
        _claimSliceService = claimSliceService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateClaimResponse>> CreateClaim(
        [FromBody] CreateClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _claimSliceService.CreateClaimAsync(
            request.StatementId,
            request.Topic,
            request.NormalizedText,
            request.Type,
            request.PersonId,
            request.CaseId,
            request.IsMaterial,
            cancellationToken);

        var response = new CreateClaimResponse(
            entity.Id,
            entity.StatementId,
            entity.PersonId,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.NormalizedText,
            entity.IsMaterial,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<GetClaimsResponse>> GetClaims(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? statementId = null,
        CancellationToken cancellationToken = default)
    {
        var (total, items) = await _claimSliceService.GetClaimsAsync(page, pageSize, statementId, cancellationToken);

        var mapped = items.Select(entity => new GetClaimsItemResponse(
            entity.Id,
            entity.StatementId,
            entity.PersonId,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.NormalizedText,
            entity.IsMaterial,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        )).ToArray();

        return Ok(new GetClaimsResponse(
            mapped,
            page,
            pageSize,
            total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        ));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetClaimResponse>> GetClaim(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _claimSliceService.GetClaimByIdAsync(id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(new GetClaimResponse(
            entity.Id,
            entity.StatementId,
            entity.PersonId,
            entity.CaseId,
            entity.Type.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.NormalizedText,
            entity.IsMaterial,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc
        ));
    }
}
