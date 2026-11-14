using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-06 (#262): the authorized entry point that triggers one raw source
/// retrieval through <see cref="IRawSourceFetcher"/>. The tenant comes only
/// from the signed <c>tenant</c> claim; only a caller whose single role claim
/// is <c>admin</c> may trigger it (it causes an outbound request), and every
/// other caller is refused before the fetcher is touched. Response size and
/// duration limits are server constants, never caller supplied. The response
/// carries the digest only: no raw bytes and no <c>ev1:</c> reference, so a
/// stored record stays ineligible for citation until the P0-07 parser runs.
/// An unknown source and another tenant's source answer identically (404).
/// </summary>
[ApiController]
[Route("api/v1/tenant-sources/{id:guid}/retrievals")]
public sealed class TenantSourceRetrievalsController : ControllerBase
{
    /// <summary>Upper bound for a request body.</summary>
    public const int MaxRequestBytes = 16 * 1024;

    /// <summary>Server-side response ceiling for one retrieval.</summary>
    public const long MaxResponseBytes = TenantRawRetrievalLimits.MaxRawBytes;

    /// <summary>Server-side wall-clock ceiling for one retrieval.</summary>
    public static readonly TimeSpan MaxTotalDuration = TimeSpan.FromSeconds(30);

    private readonly IRawSourceFetcher _fetcher;

    public TenantSourceRetrievalsController(IRawSourceFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Post(
        Guid id,
        [FromBody] TriggerRetrievalRequest? request,
        CancellationToken cancellationToken)
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
                "Only a tenant admin may trigger a source retrieval.");

        if (request is not
            {
                AttemptKey: { } attemptKey,
                RequestedUri: { } requestedUri,
                PolicyVersion: { } policyVersion,
                Jurisdiction: { } jurisdiction,
                SourceType: { } sourceType
            }
            || string.IsNullOrWhiteSpace(attemptKey)
            || attemptKey.Length > TenantFetchAttemptLimits.MaxAttemptKeyLength
            || string.IsNullOrWhiteSpace(requestedUri)
            || requestedUri.Length > TenantFetchAttemptLimits.MaxRequestedUriLength)
            return Error(StatusCodes.Status400BadRequest, "invalid_retrieval_request",
                "attemptKey, requestedUri, policyVersion, jurisdiction and sourceType are required within length limits.");

        var result = await _fetcher.FetchAsync(
            tenantId,
            new RawSourceFetchRequest(
                id,
                attemptKey,
                requestedUri,
                new FetchExpectation(policyVersion, jurisdiction, sourceType),
                new SourceFetchLimits(MaxResponseBytes, MaxTotalDuration),
                request.PinnedContentSha256),
            actor,
            cancellationToken);

        return result.Status switch
        {
            RawSourceFetchStatus.Stored =>
                StatusCode(StatusCodes.Status201Created, ToResponse(result, "stored")),
            RawSourceFetchStatus.Duplicate =>
                Ok(ToResponse(result, "duplicate")),
            RawSourceFetchStatus.Refused =>
                UnprocessableEntity(new RetrievalRefusal("retrieval_refused", result.Reason)),
            RawSourceFetchStatus.RetryLater =>
                StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new RetrievalRetryLater("retry_later", result.Reason, result.RetryDueAtUtc)),
            RawSourceFetchStatus.SourceNotFound =>
                Error(StatusCodes.Status404NotFound, "source_not_found", "The source was not found."),
            RawSourceFetchStatus.IdempotencyConflict =>
                Error(StatusCodes.Status409Conflict, "attempt_key_conflict",
                    "The attempt key was already used for a different request."),
            _ => throw new InvalidOperationException($"Unexpected status {result.Status}.")
        };
    }

    private static RetrievalResponse ToResponse(RawSourceFetchResult result, string status) =>
        new(status, result.RawRetrievalId, result.ContentSha256);

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Request body; every member except the pin is required.</summary>
public sealed record TriggerRetrievalRequest(
    string? AttemptKey,
    string? RequestedUri,
    string? PolicyVersion,
    string? Jurisdiction,
    string? SourceType,
    string? PinnedContentSha256 = null);

public sealed record RetrievalResponse(string Status, Guid? RawRetrievalId, string? ContentSha256);

public sealed record RetrievalRefusal(string Code, string? Reason);

public sealed record RetrievalRetryLater(string Code, string? Reason, DateTimeOffset? RetryDueAtUtc);
