using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-06 (#276): read-only list of the retrieval attestations recorded for one stored raw
/// retrieval, so an admin can audit which retriever/publisher identity and authentication result back
/// it. Same fail-closed gate as the sibling retrieval, parse and verification endpoints: the tenant
/// comes only from the signed <c>tenant</c> claim, the caller needs a usable subject name and a single
/// <c>admin</c> role, and every other caller is refused before a ledger is touched. Unknown, empty and
/// another tenant's retrieval answer the same 404. The response is metadata only: it never carries
/// raw bytes, the attestation (idempotency) key, the binding reference or an <c>ev1:</c> reference.
/// </summary>
[ApiController]
[Route("api/v1/tenant-raw-retrievals/{id:guid}/attestations")]
public sealed class TenantRawRetrievalAttestationsController : ControllerBase
{
    private readonly ITenantRawRetrievalLedger _retrievals;
    private readonly ITenantRetrievalAttestationLedger _attestations;

    public TenantRawRetrievalAttestationsController(
        ITenantRawRetrievalLedger retrievals, ITenantRetrievalAttestationLedger attestations)
    {
        _retrievals = retrievals;
        _attestations = attestations;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken)
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
                "Only a tenant admin may read retrieval attestations.");

        if (id == Guid.Empty || await _retrievals.GetAsync(tenantId, id, cancellationToken) is null)
            return Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found.");

        var rows = await _attestations.ListForRetrievalAsync(tenantId, id, cancellationToken);
        return Ok(rows
            .OrderBy(a => a.AttestedAtUtc).ThenBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .Select(a => new RetrievalAttestationSummary(
                a.Id, a.RetrieverIdentity, a.PublisherIdentity, a.BindingMechanism.ToString(),
                a.AuthenticationResult.ToString(), a.Jurisdiction, a.SourceType.ToString(),
                a.AttestedAtUtc, a.CreatedAtUtc))
            .ToList());
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Attestation metadata only; never the attestation key, binding reference, bytes or an evidence reference.</summary>
public sealed record RetrievalAttestationSummary(
    Guid Id,
    string RetrieverIdentity,
    string PublisherIdentity,
    string BindingMechanism,
    string AuthenticationResult,
    string Jurisdiction,
    string SourceType,
    DateTime AttestedAtUtc,
    DateTime CreatedAtUtc);
