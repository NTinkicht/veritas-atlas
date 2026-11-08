using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Parsing;

namespace VeritasAtlas.Api.Controllers;

public sealed record ClaimCandidateView(
    string ClaimKey, string Quotation, string ElementId, int Start, int Length, string ChunkTextSha256,
    string Status, bool Answerable);

public sealed record ClaimCandidateFreshnessView(string State, bool Eligible, string? Reason, string? EvaluatedAt);

public sealed record ClaimCandidatesResponse(
    string EvidenceRef, string ExtractorName, string ExtractorVersion,
    ClaimCandidateFreshnessView Freshness, IReadOnlyList<ClaimCandidateView> Claims);

public sealed record ClaimCandidatesError(string Code, string Message);

/// <summary>
/// VER-P0-11 slice 2: candidate claims (exact quotation + chunk span) for one verified evidence item of the
/// caller's tenant. The tenant comes only from the signed <c>tenant</c> claim (403 without one). Read-only:
/// nothing is stored, every candidate is unconfirmed and not answerable, and unknown, unverified and foreign
/// references are one uniform 404.
/// </summary>
[ApiController]
[Route("api/v1/tenant-evidence")]
public sealed class TenantClaimCandidatesController : ControllerBase
{
    private readonly IClaimCandidateService _candidates;

    public TenantClaimCandidatesController(IClaimCandidateService candidates)
    {
        _candidates = candidates;
    }

    [HttpGet("claim-candidates")]
    public async Task<IActionResult> List([FromQuery] string? evidenceRef, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
            return Error(StatusCodes.Status403Forbidden, "tenant_required", "The access token carries no valid organization tenant.");

        var result = await _candidates.ListAsync(tenantId, evidenceRef ?? string.Empty, cancellationToken);
        return result.Outcome switch
        {
            ClaimCandidateOutcome.Ok => Ok(new ClaimCandidatesResponse(
                evidenceRef!, ClaimExtractionV1.Name, ClaimExtractionV1.Version,
                new ClaimCandidateFreshnessView(
                    result.Freshness!.State.ToString().ToLowerInvariant(), result.Freshness.Eligible,
                    result.Freshness.Reason, result.Freshness.EvaluatedAt),
                result.Claims.Select(c => new ClaimCandidateView(
                    c.ClaimKey, c.Quotation, c.Span.ElementId, c.Span.Start, c.Span.Length, c.Span.ChunkTextSha256,
                    c.Status.ToString().ToLowerInvariant(), false)).ToList())),
            ClaimCandidateOutcome.InvalidRequest => Error(StatusCodes.Status400BadRequest, "invalid_evidence_ref",
                "evidenceRef is required and must be a bounded reference without control characters."),
            ClaimCandidateOutcome.NotFound => Error(StatusCodes.Status404NotFound, "evidence_not_found",
                "No verified evidence with this reference exists."),
            _ => Error(StatusCodes.Status409Conflict, "extraction_refused",
                "The stored evidence chunks failed integrity checks; no claim candidates are returned."),
        };
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new ClaimCandidatesError(code, message));
}
