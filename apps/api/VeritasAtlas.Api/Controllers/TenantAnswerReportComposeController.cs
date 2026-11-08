using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

public sealed record AnswerReportCitationView(string EvidenceRef, string Freshness);

public sealed record AnswerReportClaimView(
    string ClaimId, string Text, string TemporalScope, DateOnly? HistoricalAsOf, bool NonCurrentEvidence,
    IReadOnlyList<AnswerReportCitationView> Citations);

public sealed record AnswerReportContradictionView(string ContradictionId, IReadOnlyList<string> ClaimIds);

public sealed record AnswerReportView(
    string HumanVerification, IReadOnlyList<AnswerReportClaimView> Claims, IReadOnlyList<AnswerReportContradictionView> Contradictions);

public sealed record AnswerReportComposeResponse(
    string Decision, string HumanVerificationSource, IReadOnlyList<AnswerServeBlockerView> Blockers, AnswerReportView? Report);

/// <summary>
/// VER-P0-15 slice 3: composes a source-backed report (per-claim citations, historical as-of disclosure,
/// contradiction disclosures, human verification label) over the pure <see cref="AnswerReportComposer"/>. It reuses
/// the serve-check server-side evidence resolution (tenant from the signed claim only; verification and freshness
/// never taken from the caller) and returns a report only when the fail-closed serve gate says Serve. A blocked
/// answer returns the stable blocker codes and no claim text.
/// </summary>
[ApiController]
[Route("api/v1/tenant-answers")]
public sealed class TenantAnswerReportComposeController : ControllerBase
{
    public const int MaxClaimTextLength = 2000;

    private readonly IClaimEvidenceValidationService _validator;

    public TenantAnswerReportComposeController(IClaimEvidenceValidationService validator)
    {
        _validator = validator;
    }

    [HttpPost("compose")]
    public async Task<IActionResult> Compose([FromBody] AnswerServeCheckBody? body, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
            return Error(StatusCodes.Status403Forbidden, "tenant_required", "The access token carries no valid organization tenant.");
        if (!TenantAnswerServeCheckController.HasExactlyOneRoleIn(User, TenantAnswerServeCheckController.ReaderRoles))
            return Error(StatusCodes.Status403Forbidden, "role_required", "Exactly one known workflow role is required.");

        if (!TenantAnswerServeCheckController.TryParse(body, out var verification)
            || body!.Claims!.Any(c => string.IsNullOrWhiteSpace(c!.Text) || c.Text.Length > MaxClaimTextLength || c.Text.Any(ch => char.IsControl(ch) && ch is not ('\n' or '\r' or '\t'))))
            return Error(StatusCodes.Status400BadRequest, "invalid_compose_request", "The request is invalid.");

        var claims = await TenantAnswerServeCheckController.ResolveClaimsAsync(_validator, tenantId, body, cancellationToken);
        if (claims is null)
            return Error(StatusCodes.Status400BadRequest, "invalid_compose_request", "The request is invalid.");

        var texts = body.Claims!.ToDictionary(c => c!.ClaimId!, c => c!.Text!, StringComparer.Ordinal);
        var contradictions = (body.Contradictions ?? [])
            .Select(c => new KnownContradiction(c!.ContradictionId!, c.ClaimIds!.Select(x => x!).ToList()))
            .ToList();
        var disclosed = (body.DisclosedContradictionIds ?? []).Select(x => x!).ToHashSet(StringComparer.Ordinal);

        var composed = AnswerReportComposer.Compose(tenantId.ToString("D"), claims, texts, contradictions, disclosed, verification);
        var report = composed.Report is null
            ? null
            : new AnswerReportView(
                composed.Report.HumanVerification,
                composed.Report.Claims.Select(c => new AnswerReportClaimView(
                    c.ClaimId, c.Text, c.TemporalScope, c.HistoricalAsOf, c.NonCurrentEvidence,
                    c.Citations.Select(x => new AnswerReportCitationView(x.EvidenceRef, x.Freshness)).ToList())).ToList(),
                composed.Report.Contradictions.Select(k => new AnswerReportContradictionView(k.ContradictionId, k.ClaimIds)).ToList());
        return Ok(new AnswerReportComposeResponse(
            composed.Verdict.Decision.ToString().ToLowerInvariant(),
            "caller_asserted",
            composed.Verdict.Blockers.Select(b => new AnswerServeBlockerView(b.Code, b.ClaimId, b.Detail)).ToList(),
            report));
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new AnswerServeCheckError(code, message));
}
