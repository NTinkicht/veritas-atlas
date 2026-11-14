using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-06 (#270): read-only metadata of one stored raw retrieval, so an admin can re-inspect the
/// final URI, digest and length before parsing or verifying it. Same fail-closed gate as the sibling
/// retrieval, parse and verification triggers: the tenant comes only from the signed <c>tenant</c>
/// claim, the caller needs a usable subject name and a single <c>admin</c> role, and every other
/// caller is refused before the ledger is touched. Unknown, empty and another tenant's IDs answer the
/// same 404. The response never carries raw bytes, the idempotency key or an <c>ev1:</c> reference.
/// </summary>
[ApiController]
[Route("api/v1/tenant-raw-retrievals")]
public sealed class TenantRawRetrievalsController : ControllerBase
{
    private readonly ITenantRawRetrievalLedger _ledger;

    public TenantRawRetrievalsController(ITenantRawRetrievalLedger ledger)
    {
        _ledger = ledger;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
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
                "Only a tenant admin may read a raw retrieval.");

        var record = id == Guid.Empty ? null : await _ledger.GetAsync(tenantId, id, cancellationToken);
        if (record is null)
            return Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found.");

        return Ok(new RawRetrievalResponse(
            record.Id, record.SourceEntryId, record.FinalUri, record.ContentSha256,
            record.ByteLength, record.RetrievedAtUtc, record.CreatedAtUtc));
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Metadata only; never raw bytes, the idempotency key or an evidence reference.</summary>
public sealed record RawRetrievalResponse(
    Guid Id,
    Guid SourceEntryId,
    string FinalUri,
    string ContentSha256,
    long ByteLength,
    DateTime RetrievedAtUtc,
    DateTime CreatedAtUtc);
