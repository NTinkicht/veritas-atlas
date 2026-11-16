using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Parsing;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-11 slice 4: same evidence selection as <see cref="EfClaimCandidateService"/> (only chunks behind the
/// most recent verification row of the caller's tenant), then the deterministic
/// <see cref="StatementClassifierV1"/>. Nothing is written.
/// </summary>
public sealed class EfStatementClassificationService : IStatementClassificationService
{
    private readonly VeritasAtlasDbContext _db;
    private readonly IEvidenceFreshnessService _freshness;

    public EfStatementClassificationService(VeritasAtlasDbContext db, IEvidenceFreshnessService freshness)
    {
        _db = db;
        _freshness = freshness;
    }

    public async Task<StatementClassificationServiceResult> ClassifyAsync(
        Guid tenantId, string evidenceRef, string statement, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(evidenceRef)
            || evidenceRef.Length > ClaimCandidateLimits.MaxEvidenceRefLength
            || evidenceRef.Any(char.IsControl)
            || statement is null)
            return new(StatementClassificationOutcome.InvalidRequest, null, null);

        var runId = await _db.TenantEvidenceVerifications.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.ParentEvidenceRef == evidenceRef)
            .OrderByDescending(v => v.CreatedAtUtc).ThenBy(v => v.Id)
            .Select(v => (Guid?)v.ParseRunId)
            .FirstOrDefaultAsync(cancellationToken);
        if (runId is null) return new(StatementClassificationOutcome.NotFound, null, null);

        var rows = await _db.TenantEvidenceChunks.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ParseRunId == runId && c.ParentEvidenceRef == evidenceRef)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return new(StatementClassificationOutcome.Refused, null, null);

        var chunks = rows
            .Select(c => new EvidenceChunk(c.ParentEvidenceRef, c.Ordinal, c.ElementId, c.Text, c.TextSha256))
            .ToList();
        var classified = StatementClassifierV1.Classify(tenantId, statement, chunks);
        if (!classified.Succeeded)
            return classified.Failure == StatementClassificationFailure.InvalidStatement
                ? new(StatementClassificationOutcome.InvalidRequest, null, null)
                : new(StatementClassificationOutcome.Refused, null, null);

        var evaluated = await _freshness.EvaluateAsync(tenantId, evidenceRef, null, cancellationToken);
        if (evaluated.Outcome != EvidenceFreshnessOutcome.Evaluated || evaluated.Decision is null)
            return new(StatementClassificationOutcome.NotFound, null, null);
        var d = evaluated.Decision;
        return new(StatementClassificationOutcome.Ok, classified,
            new ClaimCandidateFreshness(d.State, d.Eligible, d.Reason, d.EvaluatedAt));
    }
}
