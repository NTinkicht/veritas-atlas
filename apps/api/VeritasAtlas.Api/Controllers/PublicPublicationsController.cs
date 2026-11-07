using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Controllers;

/// <summary>
/// VER-P0-16 slice 6 (#200): the anonymous public-reader projection. It is the only unauthenticated publication route.
/// Every hidden cause (draft, approved-but-unpublished, withdrawn, restricted, expired, stale, foreign tenant, unknown id)
/// produces the identical 404 body, and the success body is the exact allow-list below.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/public/publications")]
public sealed class PublicPublicationsController : ControllerBase
{
    private readonly IPublicPublicationReader _reader;

    public PublicPublicationsController(IPublicPublicationReader reader)
    {
        _reader = reader;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        // Identical on every outcome (visible or hidden) so headers never reveal why a revision is hidden,
        // and no intermediary may retain a response past publish/withdraw.
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        var view = await _reader.GetAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound(new PublicNotFound(PublicReaderDecision.AnonymousHiddenOutcome));
        }

        return Ok(new PublicPublicationResponse(view.Id, view.FreshnessLabel, view.Citations));
    }

    public sealed record PublicNotFound(string Error);

    public sealed record PublicPublicationResponse(Guid Id, string FreshnessLabel, IReadOnlyList<PublicCitationProjection> Citations);
}
