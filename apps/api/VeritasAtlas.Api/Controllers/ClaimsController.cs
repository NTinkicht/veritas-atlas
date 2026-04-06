using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Claims;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/claims")]
public sealed class ClaimsController : ControllerBase
{
    private readonly IClaimService _claimService;

    public ClaimsController(IClaimService claimService)
    {
        _claimService = claimService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateClaimResponse>> CreateClaim(
        [FromBody] CreateClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _claimService.CreateClaimAsync(
            request.StatementId,
            request.Text,
            request.PersonId,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateClaimResponse(
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

        return Ok(response);
    }
}
