using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

public sealed record EvidenceSearchError(string Code, string Message);

public sealed record EvidenceSearchResponse(IReadOnlyList<EvidenceSearchHit> Results, string? NextCursor);

/// <summary>
/// VER-P0-09 (#159): tenant-scoped search over verified evidence. The tenant comes only from the
/// signed <c>tenant</c> claim (403 without one); the endpoint is authenticated by default and open to
/// every member of the tenant. It never returns a total count, raw bytes or secrets.
/// </summary>
[ApiController]
[Route("api/v1/tenant-evidence")]
public sealed class TenantEvidenceSearchController : ControllerBase
{
    private readonly IEvidenceSearchService _search;

    public TenantEvidenceSearchController(IEvidenceSearchService search)
    {
        _search = search;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] Guid? source,
        [FromQuery] string? jurisdiction,
        [FromQuery] string? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? freshness,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        if (TenantClaim.GetTenantId(User) is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new EvidenceSearchError(
                "tenant_required", "The access token carries no valid organization tenant."));
        }

        FreshnessState? state = null;
        if (freshness is not null)
        {
            if (!Enum.TryParse<FreshnessState>(freshness, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed) || freshness.Length > 16 || char.IsDigit(freshness[0]))
            {
                return Invalid();
            }

            state = parsed;
        }

        var request = new EvidenceSearchRequest(
            q ?? string.Empty, source, jurisdiction, type,
            from?.ToUniversalTime(), to?.ToUniversalTime(), state,
            limit ?? EvidenceSearchLimits.DefaultPageSize, cursor);
        var page = await _search.SearchAsync(tenantId, request, cancellationToken);
        return page.Outcome == EvidenceSearchOutcome.Ok
            ? Ok(new EvidenceSearchResponse(page.Hits, page.NextCursor))
            : Invalid();
    }

    private ObjectResult Invalid() => BadRequest(new EvidenceSearchError(
        "invalid_search_request", "The query, a filter, the page size or the cursor is invalid."));
}
