using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-07 (#268): the authorized entry point that asks <see cref="IEvidenceVerificationService"/> to
/// mark one parse run "verified and answerable". The tenant comes only from the signed <c>tenant</c>
/// claim and only a caller whose single role claim is <c>admin</c> may trigger it; every other caller is
/// refused before any service is touched. The trusted classification the retrieval must carry is read
/// from the retrieval's source current fetch policy in the registry, never from the request, and a
/// source without a policy cannot be verified (409). The parse run must belong to the retrieval in the
/// path; an unknown run, a run of another retrieval and another tenant's rows all answer the same 404.
/// The response carries identifiers and a status only: no raw bytes, no chunk text. Until a real
/// publisher-binding verifier is supplied the shipped one never verifies, so production refuses with
/// <c>publisher_binding_unverified</c> and writes nothing.
/// </summary>
[ApiController]
[Route("api/v1/tenant-raw-retrievals/{id:guid}/parses/{parseRunId:guid}/verification")]
public sealed class TenantParseRunVerificationsController : ControllerBase
{
    private readonly IEvidenceVerificationService _verifier;
    private readonly ITenantRawRetrievalLedger _retrievals;
    private readonly ITenantParseRunLedger _runs;
    private readonly ITenantSourceFetchPolicyRegistry _policies;

    public TenantParseRunVerificationsController(
        IEvidenceVerificationService verifier,
        ITenantRawRetrievalLedger retrievals,
        ITenantParseRunLedger runs,
        ITenantSourceFetchPolicyRegistry policies)
    {
        _verifier = verifier;
        _retrievals = retrievals;
        _runs = runs;
        _policies = policies;
    }

    /// <summary>
    /// VER-P0-07 (#274): admin read of one parse run's verification state. Same fail-closed gate and
    /// identical 404 for unknown, foreign and mismatched IDs as the trigger; a run that exists but is not
    /// verified answers 404 <c>verification_not_found</c>. Identifiers and times only, never the reference.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, Guid parseRunId, CancellationToken cancellationToken)
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
                "Only a tenant admin may read a parse run's verification.");

        if (id == Guid.Empty || parseRunId == Guid.Empty) return NotFound();
        if (await _retrievals.GetAsync(tenantId, id, cancellationToken) is null) return NotFound();
        var runs = await _runs.ListForRetrievalAsync(tenantId, id, cancellationToken);
        if (runs.All(r => r.Id != parseRunId)) return NotFound();

        var verification = await _verifier.GetAsync(tenantId, parseRunId, cancellationToken);
        return verification is null
            ? Error(StatusCodes.Status404NotFound, "verification_not_found", "The parse run is not verified.")
            : Ok(ToResponse(new EvidenceVerificationResult(EvidenceVerificationOutcome.Replayed, verification, null), "verified"));
    }

    [HttpPost]
    public async Task<IActionResult> Post(Guid id, Guid parseRunId, CancellationToken cancellationToken)
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
                "Only a tenant admin may verify a parse run.");

        // All-zero IDs can never name stored rows (the services throw on them): same 404 as unknown ones.
        if (id == Guid.Empty || parseRunId == Guid.Empty)
            return NotFound();

        var retrieval = await _retrievals.GetAsync(tenantId, id, cancellationToken);
        if (retrieval is null) return NotFound();
        var runs = await _runs.ListForRetrievalAsync(tenantId, id, cancellationToken);
        if (runs.All(r => r.Id != parseRunId)) return NotFound();

        var policy = await _policies.GetCurrentAsync(tenantId, retrieval.SourceEntryId, cancellationToken);
        if (policy is null)
            return Error(StatusCodes.Status409Conflict, "source_policy_missing",
                "The source has no fetch policy to take the trusted classification from.");

        var result = await _verifier.VerifyAsync(
            tenantId, parseRunId,
            new EvidenceClassificationExpectation(policy.Policy.Jurisdiction, policy.Policy.SourceType),
            actor, cancellationToken);
        return result.Outcome switch
        {
            EvidenceVerificationOutcome.Verified =>
                StatusCode(StatusCodes.Status201Created, ToResponse(result, "verified")),
            EvidenceVerificationOutcome.Replayed =>
                Ok(ToResponse(result, "replayed")),
            EvidenceVerificationOutcome.Refused =>
                UnprocessableEntity(new VerificationResponse("refused", null, null, result.RefusalReason)),
            EvidenceVerificationOutcome.RunNotFound => NotFound(),
            _ => throw new InvalidOperationException($"Unexpected outcome {result.Outcome}.")
        };
    }

    private static VerificationResponse ToResponse(EvidenceVerificationResult result, string status) =>
        new(status, result.Verification?.Id, result.Verification?.VerifiedAtUtc, null);

    private ObjectResult NotFound() =>
        Error(StatusCodes.Status404NotFound, "parse_run_not_found", "The parse run was not found.");

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Status and identifiers only; never raw bytes, chunk text or the evidence reference.</summary>
public sealed record VerificationResponse(
    string Status, Guid? VerificationId, DateTime? VerifiedAtUtc, string? RefusalReason);
