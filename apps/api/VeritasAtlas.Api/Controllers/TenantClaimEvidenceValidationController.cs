using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

public sealed record ClaimEvidenceCitationBody(string? EvidenceRef, string? ParserName, string? ParserVersion, Guid? ScopeSourceEntryId);

public sealed record ClaimEvidenceValidationBody(
    bool EvidenceRequired, bool HistoricalEvidenceAllowed, bool HistoricalTimeContextPreserved,
    IReadOnlyList<ClaimEvidenceCitationBody>? Citations);

public sealed record ClaimEvidenceValidationError(string Code, string Message);

/// <summary>
/// VER-P0-12 (#182): tenant-scoped claim-to-evidence validation. The tenant comes only from the
/// signed <c>tenant</c> claim (403 without one); the endpoint is authenticated by default. Valid
/// claims answer 200; a rejected claim answers 422 with the stable failure codes and never any source
/// payload. The freshness evaluation instant is always the server clock.
/// </summary>
[ApiController]
[Route("api/v1/tenant-claims")]
public sealed class TenantClaimEvidenceValidationController : ControllerBase
{
    private readonly IClaimEvidenceValidationService _validator;

    public TenantClaimEvidenceValidationController(IClaimEvidenceValidationService validator)
    {
        _validator = validator;
    }

    [HttpPost("validate-evidence")]
    public async Task<IActionResult> Validate([FromBody] ClaimEvidenceValidationBody? body, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ClaimEvidenceValidationError(
                "tenant_required", "The access token carries no valid organization tenant."));
        }

        if (body?.Citations is null)
            return BadRequest(new ClaimEvidenceValidationError("invalid_claim_evidence_request", "The request is invalid."));

        var request = new ClaimEvidenceValidationRequest(
            body.EvidenceRequired,
            new SourceFreshnessClaimPolicy(body.HistoricalEvidenceAllowed, body.HistoricalTimeContextPreserved),
            body.Citations.Select(c => c is null
                ? null!
                : new ClaimEvidenceCitation(c.EvidenceRef, c.ParserName, c.ParserVersion, c.ScopeSourceEntryId)).ToList());
        var result = await _validator.ValidateAsync(tenantId, request, cancellationToken);
        return result.Outcome switch
        {
            ClaimEvidenceValidationOutcome.InvalidRequest => BadRequest(new ClaimEvidenceValidationError(
                "invalid_claim_evidence_request", "The request is invalid.")),
            _ when result.Valid => Ok(result),
            _ => UnprocessableEntity(result),
        };
    }
}
