using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
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
    // Derived, never stored: only a PUBLISHED revision is returned and publishing requires independent approval.
    // Carries no reviewer identity.
    public const string IndependentlyReviewedLabel = "independently-reviewed";

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

        return Ok(new PublicPublicationResponse(view.Id, view.FreshnessLabel, IndependentlyReviewedLabel, view.Citations));
    }

    /// <summary>
    /// VER-P1-05 (#404): machine-readable, citation-preserving export of exactly what <see cref="Get"/> may show. It reuses the
    /// same reader (hence the same fail-closed gate and identical hidden-cause 404), and carries an RFC 9530 Content-Digest
    /// over the exact body bytes so a downloaded copy can be checked for tampering.
    /// </summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        var view = await _reader.GetAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound(new PublicNotFound(PublicReaderDecision.AnonymousHiddenOutcome));
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new PublicPublicationExport(ExportFormat, view.Id, view.FreshnessLabel, IndependentlyReviewedLabel, view.Citations),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Response.Headers["Content-Digest"] = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:";
        Response.Headers.ContentDisposition = $"attachment; filename=\"publication-{view.Id:N}.json\"";
        return File(body, "application/json");
    }

    /// <summary>
    /// VER-P1-05 (#406): accessible, self-contained HTML rendering of exactly what <see cref="Get"/> may show. Same reader (same
    /// fail-closed gate and identical hidden-cause 404), semantic markup only (no script, style or external resource), every
    /// value HTML-encoded, a link only for an https source URI, and an RFC 9530 Content-Digest over the exact body bytes.
    /// </summary>
    [HttpGet("{id:guid}/export.html")]
    public async Task<IActionResult> ExportHtml(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        var view = await _reader.GetAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound(new PublicNotFound(PublicReaderDecision.AnonymousHiddenOutcome));
        }

        var body = Encoding.UTF8.GetBytes(RenderHtml(view.FreshnessLabel, view.Citations));
        Response.Headers["Content-Digest"] = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:";
        Response.Headers.ContentDisposition = $"attachment; filename=\"publication-{view.Id:N}.html\"";
        return File(body, "text/html; charset=utf-8");
    }

    private static string RenderHtml(string freshnessLabel, IReadOnlyList<PublicCitationProjection> citations)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        static string D(DateTimeOffset? v) => v is null ? string.Empty : v.Value.ToString("yyyy-MM-dd'T'HH:mm:ssK", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<title>Published report with citations</title>\n</head>\n<body>\n<main>\n<h1>Published report with citations</h1>\n")
          .Append("<p>Freshness: ").Append(E(freshnessLabel)).Append("</p>\n<p>Review: ").Append(E(IndependentlyReviewedLabel)).Append("</p>\n")
          .Append("<h2>Citations</h2>\n");
        if (citations.Count == 0) sb.Append("<p>This report has no citations.</p>\n");
        foreach (var c in citations)
        {
            sb.Append("<article>\n<h3>").Append(E(c.CitationLabel)).Append(' ').Append(E(c.Title)).Append("</h3>\n")
              .Append("<blockquote><p>").Append(E(c.Quote)).Append("</p></blockquote>\n<dl>\n")
              .Append("<dt>Source</dt><dd>").Append(E(c.SourceName)).Append("</dd>\n");
            if (c.SourceUri is { } uri && Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
            {
                sb.Append("<dt>Source link</dt><dd><a href=\"").Append(E(parsed.AbsoluteUri)).Append("\" rel=\"noopener noreferrer\">").Append(E(parsed.AbsoluteUri)).Append("</a></dd>\n");
            }
            sb.Append("<dt>Freshness</dt><dd>").Append(E(c.FreshnessLabel)).Append("</dd>\n");
            if (c.AsOf is not null) sb.Append("<dt>As of</dt><dd>").Append(E(D(c.AsOf))).Append("</dd>\n");
            if (c.EffectiveDate is not null) sb.Append("<dt>Effective date</dt><dd>").Append(E(D(c.EffectiveDate))).Append("</dd>\n");
            if (c.Certainty is not null) sb.Append("<dt>Confidence</dt><dd>").Append(E(c.Certainty)).Append("</dd>\n");
            if (c.ContradictionDisclosure is not null) sb.Append("<dt>Contradiction disclosure</dt><dd>").Append(E(c.ContradictionDisclosure)).Append("</dd>\n");
            if (c.UncertaintyLimit is not null) sb.Append("<dt>Uncertainty limit</dt><dd>").Append(E(c.UncertaintyLimit)).Append("</dd>\n");
            sb.Append("</dl>\n</article>\n");
        }
        return sb.Append("</main>\n</body>\n</html>\n").ToString();
    }

    public const string ExportFormat = "veritas-atlas.publication-export/v1";

    public sealed record PublicPublicationExport(string Format, Guid Id, string FreshnessLabel, string HumanReviewLabel, IReadOnlyList<PublicCitationProjection> Citations);

    public sealed record PublicNotFound(string Error);

    public sealed record PublicPublicationResponse(Guid Id, string FreshnessLabel, string HumanReviewLabel, IReadOnlyList<PublicCitationProjection> Citations);
}
