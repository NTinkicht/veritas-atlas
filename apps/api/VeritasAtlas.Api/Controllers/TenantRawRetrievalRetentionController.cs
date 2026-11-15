using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-18 slice 2 (#298): read-only dry run of <see cref="RetentionDeletionGate"/> for one stored raw
/// retrieval. It never deletes anything. The tenant comes only from the signed <c>tenant</c> claim, the caller
/// needs a usable subject name and a single <c>admin</c> role, and every other caller is refused before a
/// ledger is touched. Unknown, empty and another tenant's retrieval answer the same 404. No retention window
/// or legal hold is stored yet, so the retain-until input is absent and the gate can only deny: a parse run
/// is a live reference, no attestation means no audit trail, otherwise the window has not elapsed. The
/// response never carries raw bytes, idempotency keys or <c>ev1:</c> references.
/// </summary>
[ApiController]
[Route("api/v1/tenant-raw-retrievals/{id:guid}/retention-eligibility")]
public sealed class TenantRawRetrievalRetentionController : ControllerBase
{
    private readonly ITenantRawRetrievalLedger _retrievals;
    private readonly ITenantRetrievalAttestationLedger _attestations;
    private readonly ITenantParseRunLedger _runs;

    public TenantRawRetrievalRetentionController(
        ITenantRawRetrievalLedger retrievals, ITenantRetrievalAttestationLedger attestations, ITenantParseRunLedger runs)
    {
        _retrievals = retrievals;
        _attestations = attestations;
        _runs = runs;
    }

    [HttpGet]
    public async Task<IActionResult> Evaluate(Guid id, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
            return Error(StatusCodes.Status403Forbidden, "tenant_required",
                "The access token carries no valid organization tenant.");

        var actor = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(actor)
            || actor.Length > TenantAuditEventLimits.MaxSubjectLength
            || actor.Any(char.IsControl))
            return Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name.");

        if (!TenantAdminRole.IsSatisfiedBy(User))
            return Error(StatusCodes.Status403Forbidden, "admin_required",
                "Only a tenant admin may evaluate retention eligibility.");

        if (id == Guid.Empty || await _retrievals.GetAsync(tenantId, id, cancellationToken) is null)
            return Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found.");

        var attestationCount = (await _attestations.ListForRetrievalAsync(tenantId, id, cancellationToken)).Count;
        var parseRunCount = (await _runs.ListForRetrievalAsync(tenantId, id, cancellationToken)).Count;

        var decision = RetentionDeletionGate.Evaluate(new RetentionRequest(
            RetentionRecordKind.RawRetrieval, tenantId.ToString("D"), tenantId.ToString("D"), "dry-run",
            RetainUntil: null, Now: DateTimeOffset.UtcNow, LegalHold: false,
            HasLiveReference: parseRunCount > 0, AuditTrailPresent: attestationCount > 0));

        return Ok(new RetentionEligibilityResponse(
            decision.Outcome.ToString(), decision.DenialCode, parseRunCount, attestationCount));
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Dry-run verdict and the counts behind it; never bytes, keys or evidence references.</summary>
public sealed record RetentionEligibilityResponse(string Outcome, string? DenialCode, int ParseRunCount, int AttestationCount);
