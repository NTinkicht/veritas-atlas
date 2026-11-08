using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

public sealed record AnswerServeCitationBody(string? EvidenceRef, string? ParserName, string? ParserVersion, Guid? ScopeSourceEntryId);

public sealed record AnswerServeClaimBody(
    string? ClaimId, bool CurrentState, DateOnly? HistoricalAsOf, IReadOnlyList<AnswerServeCitationBody?>? Citations);

public sealed record AnswerServeContradictionBody(string? ContradictionId, IReadOnlyList<string?>? ClaimIds);

public sealed record AnswerServeCheckBody(
    IReadOnlyList<AnswerServeClaimBody?>? Claims, IReadOnlyList<AnswerServeContradictionBody?>? Contradictions,
    IReadOnlyList<string?>? DisclosedContradictionIds, string? HumanVerification);

public sealed record AnswerServeBlockerView(string Code, string? ClaimId, string? Detail);

public sealed record AnswerServeCheckResponse(
    string Decision, string HumanVerificationSource, IReadOnlyList<AnswerServeBlockerView> Blockers);

public sealed record AnswerServeCheckError(string Code, string Message);

/// <summary>
/// VER-P0-15 slice 2 (#220): stateless, server-derived serve preflight over the pure <see cref="AnswerServeGate"/>.
/// The tenant comes only from the signed <c>tenant</c> claim. Whether a citation is verified and how fresh it
/// is are never taken from the caller: each citation is resolved tenant-scoped through
/// <see cref="IClaimEvidenceValidationService"/> at the server clock, with the current-only policy for
/// current-state claims and the historical policy only when an as-of date accompanies a non-current claim.
/// Failure codes are the gate's stable blocker codes and never carry a source payload. The human verification
/// state is caller-asserted (no answer entity exists yet) and is labelled as such in the response.
/// </summary>
[ApiController]
[Route("api/v1/tenant-answers")]
public sealed class TenantAnswerServeCheckController : ControllerBase
{
    public const int MaxClaims = 50;
    public const int MaxCitationsPerClaim = ClaimEvidenceLimits.MaxCitations;
    public const int MaxTotalCitations = 200;
    public const int MaxContradictions = 200;
    public const int MaxIdLength = 500;
    private static readonly string[] ReaderRoles = ["operator", "analyst", "reviewer", "publisher", "admin"];

    private readonly IClaimEvidenceValidationService _validator;

    public TenantAnswerServeCheckController(IClaimEvidenceValidationService validator)
    {
        _validator = validator;
    }

    [HttpPost("serve-check")]
    public async Task<IActionResult> Check([FromBody] AnswerServeCheckBody? body, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
            return Error(StatusCodes.Status403Forbidden, "tenant_required", "The access token carries no valid organization tenant.");
        if (!HasExactlyOneRoleIn(User, ReaderRoles))
            return Error(StatusCodes.Status403Forbidden, "role_required", "Exactly one known workflow role is required.");

        if (!TryParse(body, out var verification))
            return Error(StatusCodes.Status400BadRequest, "invalid_serve_check_request", "The request is invalid.");

        var tenantKey = tenantId.ToString("D");
        var claims = new List<ServeClaim>(body!.Claims!.Count);
        foreach (var claim in body.Claims!)
        {
            var citations = new List<ServeCitation>();
            var policy = claim!.CurrentState
                ? SourceFreshnessClaimPolicy.CurrentOnly
                : new SourceFreshnessClaimPolicy(true, claim.HistoricalAsOf is not null);
            foreach (var citation in claim.Citations ?? [])
            {
                var result = await _validator.ValidateAsync(
                    tenantId,
                    new ClaimEvidenceValidationRequest(
                        true, policy,
                        [new ClaimEvidenceCitation(citation!.EvidenceRef, citation.ParserName, citation.ParserVersion, citation.ScopeSourceEntryId)]),
                    cancellationToken);
                if (result.Outcome == ClaimEvidenceValidationOutcome.InvalidRequest)
                    return Error(StatusCodes.Status400BadRequest, "invalid_serve_check_request", "The request is invalid.");

                var item = result.Items.FirstOrDefault();
                // A stale or unknown-freshness result still proves the evidence exists and is authentic in this
                // tenant, so the gate attributes it to freshness rather than to verification. Every other failure
                // (unknown, tampered, wrong scope or parser, unauthenticated) leaves the citation unverified.
                var freshnessOnly = item is not null
                    && (item.Code is ClaimEvidenceCodes.Stale or ClaimEvidenceCodes.FreshnessUnknown);
                var verified = result.Valid || freshnessOnly;
                citations.Add(new ServeCitation(
                    tenantKey, citation.EvidenceRef ?? string.Empty, verified, verified ? item?.Freshness : null));
            }

            claims.Add(new ServeClaim(claim.ClaimId!, claim.CurrentState, claim.HistoricalAsOf, citations));
        }

        var contradictions = (body.Contradictions ?? [])
            .Select(c => new KnownContradiction(c!.ContradictionId!, c.ClaimIds!.Select(x => x!).ToList()))
            .ToList();
        var disclosed = (body.DisclosedContradictionIds ?? []).Select(x => x!).ToHashSet(StringComparer.Ordinal);

        var verdict = AnswerServeGate.Evaluate(tenantKey, claims, contradictions, disclosed, verification);
        return Ok(new AnswerServeCheckResponse(
            verdict.Decision.ToString().ToLowerInvariant(),
            "caller_asserted",
            verdict.Blockers.Select(b => new AnswerServeBlockerView(b.Code, b.ClaimId, b.Detail)).ToList()));
    }

    private static bool TryParse(AnswerServeCheckBody? body, out HumanVerificationState verification)
    {
        verification = HumanVerificationState.Unreviewed;
        if (body is null
            || !Enum.TryParse(body.HumanVerification, true, out verification)
            || !Enum.IsDefined(verification)
            || body.HumanVerification!.Any(char.IsDigit))
            return false;

        var claims = body.Claims;
        if (claims is null || claims.Count == 0 || claims.Count > MaxClaims || claims.Any(c => c is null || !ValidId(c.ClaimId)))
            return false;
        if (claims.Select(c => c!.ClaimId).Distinct(StringComparer.Ordinal).Count() != claims.Count) return false;
        var total = 0;
        foreach (var c in claims)
        {
            var n = c!.Citations?.Count ?? 0;
            total += n;
            if (n > MaxCitationsPerClaim || total > MaxTotalCitations || (c.Citations?.Any(x => x is null) ?? false)) return false;
        }

        var contradictions = body.Contradictions ?? [];
        if (contradictions.Count > MaxContradictions
            || contradictions.Any(c => c is null || !ValidId(c.ContradictionId) || c.ClaimIds is null || c.ClaimIds.Count > MaxClaims
                                      || c.ClaimIds.Any(x => !ValidId(x))))
            return false;
        var disclosed = body.DisclosedContradictionIds ?? [];
        return disclosed.Count <= MaxContradictions && disclosed.All(ValidId);
    }

    private static bool ValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= MaxIdLength && !id.Any(char.IsControl);

    private static bool HasExactlyOneRoleIn(ClaimsPrincipal user, string[] allowed)
    {
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roles.Count == 1 && allowed.Contains(roles[0], StringComparer.OrdinalIgnoreCase);
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new AnswerServeCheckError(code, message));
}
