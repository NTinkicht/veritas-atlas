using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Parsing;

namespace VeritasAtlas.Api.Controllers;

public sealed record StatementClassificationBody(string? EvidenceRef, string? Statement);

public sealed record StatementSpanView(string ElementId, int Start, int Length, string ChunkTextSha256);

public sealed record StatementClassificationResponse(
    string EvidenceRef, string ClassifierName, string ClassifierVersion, string Class,
    string? SupportingEvidenceRef, string? SupportingElementId, StatementSpanView? Span,
    bool Answerable, ClaimCandidateFreshnessView Freshness);

public sealed record StatementClassificationError(string Code, string Message);

/// <summary>
/// VER-P0-11 slice 4: classify one proposed statement (quotation, inference, unresolved, unsupported) against one
/// verified evidence item of the caller's tenant. The tenant comes only from the signed <c>tenant</c> claim
/// (403 without one). Read-only and never answerable: a classification is not a claim. Unknown, unverified and
/// foreign references are one uniform 404. The statement is treated as data and is never echoed in errors.
/// </summary>
[ApiController]
[Route("api/v1/tenant-evidence")]
public sealed class TenantStatementClassificationController : ControllerBase
{
    private readonly IStatementClassificationService _classifier;

    public TenantStatementClassificationController(IStatementClassificationService classifier)
    {
        _classifier = classifier;
    }

    [HttpPost("statement-classification")]
    public async Task<IActionResult> Classify([FromBody] StatementClassificationBody? body, CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
            return Error(StatusCodes.Status403Forbidden, "tenant_required", "The access token carries no valid organization tenant.");
        if (body?.EvidenceRef is null || body.Statement is null)
            return Error(StatusCodes.Status400BadRequest, "invalid_statement_request",
                "evidenceRef and statement are required, bounded and without control characters.");

        var result = await _classifier.ClassifyAsync(tenantId, body.EvidenceRef, body.Statement, cancellationToken);
        return result.Outcome switch
        {
            StatementClassificationOutcome.Ok => Ok(new StatementClassificationResponse(
                body.EvidenceRef, StatementClassifierV1.Name, StatementClassifierV1.Version,
                result.Classification!.Class!.Value.ToString().ToLowerInvariant(),
                result.Classification.SupportingEvidenceRef, result.Classification.SupportingElementId,
                result.Classification.Span is { } s ? new StatementSpanView(s.ElementId, s.Start, s.Length, s.ChunkTextSha256) : null,
                false,
                new ClaimCandidateFreshnessView(
                    result.Freshness!.State.ToString().ToLowerInvariant(), result.Freshness.Eligible,
                    result.Freshness.Reason, result.Freshness.EvaluatedAt))),
            StatementClassificationOutcome.InvalidRequest => Error(StatusCodes.Status400BadRequest, "invalid_statement_request",
                "evidenceRef and statement are required, bounded and without control characters."),
            StatementClassificationOutcome.NotFound => Error(StatusCodes.Status404NotFound, "evidence_not_found",
                "No verified evidence with this reference exists."),
            _ => Error(StatusCodes.Status409Conflict, "classification_refused",
                "The stored evidence chunks failed integrity checks; no classification is returned."),
        };
    }

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new StatementClassificationError(code, message));
}
