using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-17 (#342): tenant-scoped read of one VERIFIED evidence item. The reference must carry an
/// append-only verification row of the caller's tenant, so raw, parsed-only, unknown and foreign
/// references all collapse into the same not-found outcome. The most recently retrieved verified copy is
/// authoritative (same rule as search and freshness); freshness is re-evaluated with the server clock on
/// every call. Chunk text comes only from the parse run behind that verification and is bounded in the
/// database.
/// </summary>
public sealed class EfEvidenceDetailService : IEvidenceDetailService
{
    private readonly VeritasAtlasDbContext _db;
    private readonly IEvidenceFreshnessService _freshness;

    public EfEvidenceDetailService(VeritasAtlasDbContext db, IEvidenceFreshnessService freshness)
    {
        _db = db;
        _freshness = freshness;
    }

    public async Task<EvidenceDetailResult> GetAsync(Guid tenantId, string evidenceRef, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(evidenceRef)
            || evidenceRef.Length > EvidenceDetailLimits.MaxRefLength
            || evidenceRef.Any(char.IsControl))
        {
            return new(EvidenceDetailOutcome.InvalidRequest, null);
        }

        var rows = await (
            from v in _db.TenantEvidenceVerifications.AsNoTracking()
            join r in _db.TenantRawRetrievals.AsNoTracking()
                on new { v.TenantId, Id = v.RawRetrievalId } equals new { r.TenantId, r.Id }
            join a in _db.TenantRetrievalAttestations.AsNoTracking()
                on new { v.TenantId, Id = v.AttestationId } equals new { a.TenantId, a.Id }
            join p in _db.TenantParseRuns.AsNoTracking()
                on new { v.TenantId, Id = v.ParseRunId } equals new { p.TenantId, p.Id } into runs
            from p in runs.DefaultIfEmpty()
            where v.TenantId == tenantId && v.ParentEvidenceRef == evidenceRef
            select new
            {
                v.ParseRunId, r.Id, r.FinalUri, r.SourceEntryId, r.RetrievedAtUtc,
                a.Jurisdiction, a.SourceType, a.AuthenticationResult,
                ParserName = p == null ? null : p.ParserName,
                ParserVersion = p == null ? null : p.ParserVersion
            }).ToListAsync(cancellationToken);
        if (rows.Count == 0) return new(EvidenceDetailOutcome.NotFound, null);

        var best = rows.OrderByDescending(x => x.RetrievedAtUtc).ThenBy(x => x.Id).First();
        var evaluated = await _freshness.EvaluateAsync(tenantId, evidenceRef, null, cancellationToken);
        if (evaluated.Outcome != EvidenceFreshnessOutcome.Evaluated || evaluated.Decision is null)
        {
            return new(EvidenceDetailOutcome.NotFound, null);
        }

        var decision = evaluated.Decision;
        var chunkRows = await _db.TenantEvidenceChunks.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ParseRunId == best.ParseRunId && c.ParentEvidenceRef == evidenceRef)
            .OrderBy(c => c.Ordinal).ThenBy(c => c.Id)
            .Take(EvidenceDetailLimits.MaxChunks + 1)
            .Select(c => new
            {
                c.Ordinal, c.ElementId,
                // Bound the projected text in the database: never more than MaxChunkLength + 1 code units leave Postgres.
                Text = c.Text.Substring(0, EvidenceDetailLimits.MaxChunkLength + 1)
            })
            .ToListAsync(cancellationToken);

        var truncated = chunkRows.Count > EvidenceDetailLimits.MaxChunks;
        var chunks = new List<EvidenceDetailChunk>();
        foreach (var row in chunkRows.Take(EvidenceDetailLimits.MaxChunks))
        {
            var text = row.Text;
            if (text.Length > EvidenceDetailLimits.MaxChunkLength)
            {
                truncated = true;
                var cut = EvidenceDetailLimits.MaxChunkLength;
                if (char.IsHighSurrogate(text[cut - 1])) cut--;
                text = text[..cut];
            }

            chunks.Add(new EvidenceDetailChunk(row.Ordinal, row.ElementId, text));
        }

        return new(EvidenceDetailOutcome.Ok, new EvidenceDetail(
            evidenceRef, best.FinalUri, best.SourceEntryId, best.Jurisdiction, best.SourceType,
            best.AuthenticationResult, DateTime.SpecifyKind(best.RetrievedAtUtc, DateTimeKind.Utc),
            decision.State, decision.Eligible, decision.Reason, decision.EvaluatedAt,
            best.ParserName, best.ParserVersion, chunks, truncated));
    }
}
