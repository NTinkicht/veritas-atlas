using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Parsing;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-11 slice 2: only chunks behind an append-only verification row of the caller's tenant are read,
/// so raw or merely parsed records never yield candidates. Nothing is written. A chunk whose digest does not
/// match its text fails the whole extraction (see <see cref="ClaimExtractionV1"/>), and the result is refused.
/// </summary>
public sealed class EfClaimCandidateService : IClaimCandidateService
{
    private readonly VeritasAtlasDbContext _db;
    private readonly IEvidenceFreshnessService _freshness;
    private readonly IClaimExtractor _extractor;

    public EfClaimCandidateService(VeritasAtlasDbContext db, IEvidenceFreshnessService freshness, IClaimExtractor extractor)
    {
        _db = db;
        _freshness = freshness;
        _extractor = extractor;
    }

    public async Task<ClaimCandidateResult> ListAsync(Guid tenantId, string evidenceRef, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(evidenceRef)
            || evidenceRef.Length > ClaimCandidateLimits.MaxEvidenceRefLength
            || evidenceRef.Any(char.IsControl))
            return new(ClaimCandidateOutcome.InvalidRequest, [], null);

        // Same rule as search/freshness: the most recently created verification of this reference is authoritative.
        var runId = await _db.TenantEvidenceVerifications.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.ParentEvidenceRef == evidenceRef)
            .OrderByDescending(v => v.CreatedAtUtc).ThenBy(v => v.Id)
            .Select(v => (Guid?)v.ParseRunId)
            .FirstOrDefaultAsync(cancellationToken);
        if (runId is null) return new(ClaimCandidateOutcome.NotFound, [], null);

        var rows = await _db.TenantEvidenceChunks.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ParseRunId == runId && c.ParentEvidenceRef == evidenceRef)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return new(ClaimCandidateOutcome.ExtractionRefused, [], null);

        var chunks = rows
            .Select(c => new EvidenceChunk(c.ParentEvidenceRef, c.Ordinal, c.ElementId, c.Text, c.TextSha256))
            .ToList();
        var extracted = _extractor.Extract(tenantId, chunks);
        if (!extracted.Succeeded) return new(ClaimCandidateOutcome.ExtractionRefused, [], null);

        var evaluated = await _freshness.EvaluateAsync(tenantId, evidenceRef, null, cancellationToken);
        if (evaluated.Outcome != EvidenceFreshnessOutcome.Evaluated || evaluated.Decision is null)
            return new(ClaimCandidateOutcome.NotFound, [], null);
        var d = evaluated.Decision;
        return new(ClaimCandidateOutcome.Ok, extracted.Claims,
            new ClaimCandidateFreshness(d.State, d.Eligible, d.Reason, d.EvaluatedAt));
    }
}
