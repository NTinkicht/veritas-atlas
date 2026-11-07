using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>PostgreSQL public-reader projection (VER-P0-16 slice 6, #200). Fail closed on any unreadable fact.</summary>
public sealed class EfPublicPublicationReader : IPublicPublicationReader
{
    private const int MaxCitations = 50;
    private static readonly JsonSerializerOptions CitationOptions = new(JsonSerializerDefaults.Web);
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _time;

    public EfPublicPublicationReader(VeritasAtlasDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<PublicPublicationView?> GetAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        // Ids are unique per tenant only; more than one match is ambiguous and therefore hidden.
        var rows = await _db.TenantPublicationRevisions.AsNoTracking()
            .Where(x => x.Id == revisionId).Take(2).ToListAsync(cancellationToken);
        if (rows.Count != 1) return null;
        var row = rows[0];

        if (!Enum.TryParse<LifecycleState>(row.State, ignoreCase: false, out var state)) return null;
        var currency = Enum.TryParse<EvidenceCurrency>(row.EvidenceCurrency, ignoreCase: false, out var parsed)
            ? parsed : EvidenceCurrency.Unknown;
        DateTimeOffset? expires = row.ExpiresAtUtc is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null;

        var decision = PublicReaderGate.Evaluate(new PublicReaderFacts(
            state, row.PolicyPermitsPublicDisplay, row.Restricted, row.Superseded, expires, _time.GetUtcNow(),
            row.FreshnessRequired, currency, row.HistoricalLabelPresent));
        if (!decision.Visible) return null;

        List<PublicCitationProjection> citations;
        try
        {
            citations = string.IsNullOrEmpty(row.PublicCitationsJson)
                ? []
                : JsonSerializer.Deserialize<List<PublicCitationProjection>>(row.PublicCitationsJson, CitationOptions) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }

        if (citations.Count > MaxCitations || citations.Any(c => c is null || c.CitationLabel is null || c.Title is null || c.Quote is null || c.SourceName is null || c.FreshnessLabel is null)) return null;
        var label = currency == EvidenceCurrency.Current ? "current" : "historical";
        return new PublicPublicationView(row.Id, label, citations);
    }
}
