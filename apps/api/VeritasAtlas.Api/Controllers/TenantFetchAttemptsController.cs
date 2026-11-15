using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-06 (#280): read-only list of the fetch attempts recorded for one source, so an admin can see
/// why a retrieval failed or was retried. Same fail-closed gate as the sibling raw-retrieval reads: the
/// tenant comes only from the signed <c>tenant</c> claim, the caller needs a usable subject name and a
/// single <c>admin</c> role, and every other caller is refused before the ledger is touched. A missing
/// or empty <c>sourceId</c> answers 400; an unknown or another tenant's source answers an empty list,
/// never an existence signal. The response never carries the attempt idempotency key.
/// </summary>
[ApiController]
[Route("api/v1/tenant-fetch-attempts")]
public sealed class TenantFetchAttemptsController : ControllerBase
{
    private readonly ITenantFetchAttemptLedger _ledger;

    public TenantFetchAttemptsController(ITenantFetchAttemptLedger ledger)
    {
        _ledger = ledger;
    }

    /// <summary>Server-side ceiling on one list response; the newest rows win.</summary>
    public const int MaxListed = 100;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid sourceId, CancellationToken cancellationToken)
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
                "Only a tenant admin may read fetch attempts.");

        if (sourceId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "source_id_required",
                "A non-empty sourceId query parameter is required.");

        var rows = await _ledger.ListRecentForSourceAsync(tenantId, sourceId, MaxListed, cancellationToken);
        return Ok(rows
            .Select(r => new FetchAttemptResponse(
                r.Id, r.SourceEntryId, r.RequestedUri, r.AttemptedAtUtc, ToWire(r.Outcome),
                r.FailureClass is { } f ? ToWire(f) : null, r.HttpStatus, r.RawRetrievalId, r.CreatedAtUtc))
            .ToList());
    }

    private static string ToWire(FetchAttemptOutcome outcome) => outcome switch
    {
        FetchAttemptOutcome.Succeeded => "succeeded",
        FetchAttemptOutcome.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private static string ToWire(FetchFailureClass failure) => failure switch
    {
        FetchFailureClass.Network => "network",
        FetchFailureClass.Timeout => "timeout",
        FetchFailureClass.HttpStatus => "http_status",
        FetchFailureClass.SizeLimit => "size_limit",
        FetchFailureClass.PolicyDenied => "policy_denied",
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Metadata only; never the attempt idempotency key, raw bytes or an evidence reference.</summary>
public sealed record FetchAttemptResponse(
    Guid Id,
    Guid SourceEntryId,
    string RequestedUri,
    DateTime AttemptedAtUtc,
    string Outcome,
    string? FailureClass,
    int? HttpStatus,
    Guid? RawRetrievalId,
    DateTime CreatedAtUtc);
