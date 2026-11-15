using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-18 slices 2-4 (#298, #300, #302): read-only history and dry run of <see cref="RetentionDeletionGate"/> for one stored raw
/// retrieval. It never deletes anything. The tenant comes only from the signed <c>tenant</c> claim, the caller
/// needs a usable subject name and a single <c>admin</c> role, and every other caller is refused before a
/// ledger is touched. Unknown, empty and another tenant's retrieval answer the same 404. No retention window
/// or legal hold recorded (slice 3, <c>PUT</c>) means the retain-until input is absent and the gate can only
/// deny: a parse run is a live reference, no attestation means no audit trail, otherwise the window has not
/// elapsed. The latest recorded retention version feeds the legal-hold and retain-until inputs. The
/// response never carries raw bytes, idempotency keys or <c>ev1:</c> references.
/// </summary>
[ApiController]
[Route("api/v1/tenant-raw-retrievals/{id:guid}")]
public sealed class TenantRawRetrievalRetentionController : ControllerBase
{
    private readonly ITenantRawRetrievalLedger _retrievals;
    private readonly ITenantRetrievalAttestationLedger _attestations;
    private readonly ITenantParseRunLedger _runs;
    private readonly ITenantRetentionRecordStore _records;

    public TenantRawRetrievalRetentionController(
        ITenantRawRetrievalLedger retrievals, ITenantRetrievalAttestationLedger attestations, ITenantParseRunLedger runs,
        ITenantRetentionRecordStore records)
    {
        _retrievals = retrievals;
        _attestations = attestations;
        _runs = runs;
        _records = records;
    }

    /// <summary>
    /// Appends the next retention version (legal hold flag and an optional retain-until that may only move
    /// later). Admin only; it records the policy input and never deletes anything.
    /// </summary>
    [HttpPut("retention")]
    public async Task<IActionResult> Record(Guid id, [FromBody] RetentionRecordRequest? request, CancellationToken cancellationToken)
    {
        if (Gate(out var tenantId, out var actor) is { } refused) return refused;
        if (request is not { LegalHold: { } legalHold })
            return Error(StatusCodes.Status400BadRequest, "retention_invalid",
                "An explicit legalHold flag is required.");
        if (id == Guid.Empty || await _retrievals.GetAsync(tenantId, id, cancellationToken) is null)
            return Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found.");

        // A new window must be in the future; resubmitting the latest recorded window unchanged is allowed
        // (even once elapsed) so a hold can be set or released. Earlier windows are still refused by the store.
        if (request.RetainUntilUtc is { } u && u.UtcDateTime <= DateTime.UtcNow)
        {
            var latest = await _records.GetLatestAsync(tenantId, id, cancellationToken);
            var ticks = u.UtcDateTime.Ticks - u.UtcDateTime.Ticks % 10;
            if (latest?.RetainUntilUtc is not { } existing || existing.Ticks - existing.Ticks % 10 != ticks)
                return Error(StatusCodes.Status400BadRequest, "retention_invalid",
                    "A retain-until instant must be in the future unless it repeats the recorded window.");
        }

        var result = await _records.AppendAsync(
            tenantId, id, legalHold, request.RetainUntilUtc?.UtcDateTime, actor, cancellationToken);
        return result.Outcome switch
        {
            RetentionRecordAppendOutcome.Appended or RetentionRecordAppendOutcome.NoChange => Ok(new RetentionRecordResponse(
                result.Record!.Version, result.Record.LegalHold, result.Record.RetainUntilUtc,
                result.Outcome == RetentionRecordAppendOutcome.Appended)),
            RetentionRecordAppendOutcome.WindowShortened => Error(StatusCodes.Status409Conflict,
                "retention_window_shortened", "A recorded retention window may only move later."),
            RetentionRecordAppendOutcome.Conflict => Error(StatusCodes.Status409Conflict,
                "retention_conflict", "The retention record changed concurrently; read it and retry."),
            _ => Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found.")
        };
    }

    /// <summary>Every recorded retention version (who set which hold or window, and when), oldest first.</summary>
    [HttpGet("retention")]
    public async Task<IActionResult> History(Guid id, CancellationToken cancellationToken)
    {
        if (Gate(out var tenantId, out _) is { } refused) return refused;
        if (id == Guid.Empty || await _retrievals.GetAsync(tenantId, id, cancellationToken) is null)
            return Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found.");

        var versions = await _records.ListAsync(tenantId, id, cancellationToken);
        return Ok(new RetentionHistoryResponse(versions.Select(v => new RetentionHistoryEntry(
            v.Version, v.LegalHold, v.RetainUntilUtc, v.ActorSubject, v.CreatedAtUtc)).ToList()));
    }

    [HttpGet("retention-eligibility")]
    public async Task<IActionResult> Evaluate(Guid id, CancellationToken cancellationToken)
    {
        if (Gate(out var tenantId, out _) is { } refused) return refused;
        if (id == Guid.Empty || await _retrievals.GetAsync(tenantId, id, cancellationToken) is null)
            return Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found.");

        var attestationCount = (await _attestations.ListForRetrievalAsync(tenantId, id, cancellationToken)).Count;
        var parseRunCount = (await _runs.ListForRetrievalAsync(tenantId, id, cancellationToken)).Count;
        var record = await _records.GetLatestAsync(tenantId, id, cancellationToken);

        var decision = RetentionDeletionGate.Evaluate(new RetentionRequest(
            RetentionRecordKind.RawRetrieval, tenantId.ToString("D"), tenantId.ToString("D"), "dry-run",
            RetainUntil: record?.RetainUntilUtc is { } until ? new DateTimeOffset(until, TimeSpan.Zero) : null,
            Now: DateTimeOffset.UtcNow, LegalHold: record?.LegalHold ?? false,
            HasLiveReference: parseRunCount > 0, AuditTrailPresent: attestationCount > 0));

        return Ok(new RetentionEligibilityResponse(
            decision.Outcome.ToString(), decision.DenialCode, parseRunCount, attestationCount,
            record?.Version ?? 0, record?.LegalHold ?? false, record?.RetainUntilUtc));
    }

    private IActionResult? Gate(out Guid tenantId, out string actor)
    {
        tenantId = Guid.Empty;
        actor = string.Empty;
        if (TenantClaim.GetTenantId(User) is not { } tenant)
            return Error(StatusCodes.Status403Forbidden, "tenant_required",
                "The access token carries no valid organization tenant.");

        var name = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > TenantAuditEventLimits.MaxSubjectLength
            || name.Any(char.IsControl))
            return Error(StatusCodes.Status403Forbidden, "actor_required",
                "The access token carries no usable subject name.");

        if (!TenantAdminRole.IsSatisfiedBy(User))
            return Error(StatusCodes.Status403Forbidden, "admin_required",
                "Only a tenant admin may manage or evaluate retention.");

        tenantId = tenant;
        actor = name;
        return null;
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Dry-run verdict and the counts behind it; never bytes, keys or evidence references.</summary>
public sealed record RetentionEligibilityResponse(
    string Outcome, string? DenialCode, int ParseRunCount, int AttestationCount,
    int RecordVersion, bool LegalHold, DateTime? RetainUntilUtc);

/// <summary>Body of the retention PUT; the retain-until must be in the future when present.</summary>
public sealed record RetentionRecordRequest(bool? LegalHold, DateTimeOffset? RetainUntilUtc);

/// <summary>Append-only retention versions of one raw retrieval; never bytes, keys or evidence references.</summary>
public sealed record RetentionHistoryResponse(IReadOnlyList<RetentionHistoryEntry> Versions);

public sealed record RetentionHistoryEntry(
    int Version, bool LegalHold, DateTime? RetainUntilUtc, string ActorSubject, DateTime CreatedAtUtc);

public sealed record RetentionRecordResponse(int Version, bool LegalHold, DateTime? RetainUntilUtc, bool Appended);
