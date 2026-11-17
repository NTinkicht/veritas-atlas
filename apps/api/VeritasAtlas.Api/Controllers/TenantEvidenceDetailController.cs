using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

public sealed record EvidenceDetailError(string Code, string Message);

/// <summary>
/// VER-P0-17 (#342): tenant-scoped read of one verified evidence item for the analyst evidence detail
/// view. The tenant comes only from the signed <c>tenant</c> claim (403 without one); unknown,
/// unverified and foreign references are the same 404. It never returns raw bytes or secrets.
/// </summary>
[ApiController]
[Route("api/v1/tenant-evidence")]
public sealed class TenantEvidenceDetailController : ControllerBase
{
    private readonly IEvidenceDetailService _detail;

    public TenantEvidenceDetailController(IEvidenceDetailService detail)
    {
        _detail = detail;
    }

    [HttpGet("detail")]
    public async Task<IActionResult> Get([FromQuery(Name = "ref")] string? evidenceRef, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new EvidenceDetailError(
                "tenant_required", "The access token carries no valid organization tenant."));
        }

        var result = await _detail.GetAsync(tenantId, evidenceRef ?? string.Empty, cancellationToken);
        return result.Outcome switch
        {
            EvidenceDetailOutcome.Ok => Ok(result.Detail),
            EvidenceDetailOutcome.NotFound => NotFound(new EvidenceDetailError(
                "evidence_not_found", "No verified evidence with that reference exists.")),
            _ => BadRequest(new EvidenceDetailError("invalid_evidence_ref", "The evidence reference is invalid.")),
        };
    }
}
