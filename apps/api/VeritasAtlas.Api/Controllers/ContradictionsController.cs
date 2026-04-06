using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Contradictions;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Enums;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/contradictions")]
public sealed class ContradictionsController : ControllerBase
{
    private readonly IContradictionService _contradictionService;

    public ContradictionsController(IContradictionService contradictionService)
    {
        _contradictionService = contradictionService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateContradictionResponse>> CreateContradiction(
        [FromBody] CreateContradictionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ContradictionType>(request.Type, true, out var contradictionType))
        {
            return BadRequest(new { message = "Invalid contradiction type." });
        }

        var entity = await _contradictionService.AddContradictionAsync(
            request.CaseId,
            request.LeftClaimId,
            request.RightClaimId,
            contradictionType,
            request.Summary,
            request.Rationale,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateContradictionResponse(
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

        return Ok(response);
    }
}
