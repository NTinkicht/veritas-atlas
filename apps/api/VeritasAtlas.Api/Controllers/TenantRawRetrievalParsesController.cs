using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-07 (#266): the authorized entry point that runs the versioned parser over one stored raw
/// retrieval through <see cref="IEvidenceParseService"/>. The tenant comes only from the signed
/// <c>tenant</c> claim; only a caller whose single role claim is <c>admin</c> may trigger it (same
/// fail-closed rule as the retrieval trigger), and every other caller is refused before the parse
/// service is touched. A parse is recorded once per raw retrieval and parser version, so a repeat
/// answers 200 with the original run. The response carries run metadata only: no raw bytes, no chunk
/// text and no <c>ev1:</c> reference, because nothing is verified here and unverified chunks stay
/// ineligible for citation. An unknown retrieval and another tenant's retrieval answer identically (404).
/// </summary>
[ApiController]
[Route("api/v1/tenant-raw-retrievals/{id:guid}/parses")]
public sealed class TenantRawRetrievalParsesController : ControllerBase
{
    private readonly IEvidenceParseService _parser;

    public TenantRawRetrievalParsesController(IEvidenceParseService parser)
    {
        _parser = parser;
    }

    [HttpPost]
    public async Task<IActionResult> Post(Guid id, CancellationToken cancellationToken)
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
                "Only a tenant admin may parse a raw retrieval.");

        var result = await _parser.ParseAsync(tenantId, id, actor, cancellationToken);
        return result.Outcome switch
        {
            EvidenceParseOutcome.ParsedUnverified =>
                StatusCode(StatusCodes.Status201Created, ToResponse(result, "parsed_unverified")),
            EvidenceParseOutcome.Replayed =>
                Ok(ToResponse(result, "replayed")),
            EvidenceParseOutcome.Failed =>
                UnprocessableEntity(ToResponse(result, "failed")),
            EvidenceParseOutcome.RetrievalNotFound =>
                Error(StatusCodes.Status404NotFound, "retrieval_not_found", "The raw retrieval was not found."),
            _ => throw new InvalidOperationException($"Unexpected outcome {result.Outcome}.")
        };
    }

    private static ParseResponse ToResponse(EvidenceParseResult result, string status) =>
        new(
            status,
            result.Run?.Id,
            result.Run?.ParserName,
            result.Run?.ParserVersion,
            result.Run?.Outcome.ToString(),
            result.Run?.FailureClass?.ToString(),
            result.ChunkCount);

    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new TenantSourceError(code, message));
}

/// <summary>Run metadata only; never raw bytes, chunk text or an evidence reference.</summary>
public sealed record ParseResponse(
    string Status,
    Guid? ParseRunId,
    string? ParserName,
    string? ParserVersion,
    string? RunOutcome,
    string? FailureClass,
    int ChunkCount);
