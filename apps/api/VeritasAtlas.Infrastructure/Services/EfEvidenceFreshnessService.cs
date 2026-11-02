using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-08 (#175): evaluates <c>source-freshness/v1</c> for a verified evidence reference.
/// Only evidence with an append-only verification row in the caller's tenant is evaluated
/// (otherwise <c>NotFound</c>); the retrieval time is read from the stored raw retrieval, the
/// policy and revision proof come from the injected trusted boundaries, and the evaluation
/// instant is the server clock. Missing policy or revision proof classifies Unknown (fail closed).
/// </summary>
public sealed class EfEvidenceFreshnessService : IEvidenceFreshnessService
{
    private readonly VeritasAtlasDbContext _db;
    private readonly ISourceFreshnessPolicyResolver _policies;
    private readonly ISourceRevisionEvidenceProvider _revisions;
    private readonly TimeProvider _timeProvider;

    public EfEvidenceFreshnessService(
        VeritasAtlasDbContext db, ISourceFreshnessPolicyResolver policies,
        ISourceRevisionEvidenceProvider revisions, TimeProvider timeProvider)
    {
        _db = db;
        _policies = policies;
        _revisions = revisions;
        _timeProvider = timeProvider;
    }

    public async Task<EvidenceFreshnessResult> EvaluateAsync(
        Guid tenantId, string evidenceRef, SourceFreshnessClaimPolicy? claimPolicy, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(evidenceRef) || evidenceRef.Length > 512 || evidenceRef.Any(char.IsControl))
            throw new ArgumentException("Invalid evidence reference.", nameof(evidenceRef));

        var verification = await _db.TenantEvidenceVerifications.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ParentEvidenceRef == evidenceRef)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (verification is null) return NotFound();

        var retrieval = await _db.TenantRawRetrievals.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == verification.RawRetrievalId)
            .Select(x => new { x.Id, x.SourceEntryId, x.RetrievedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (retrieval is null) return NotFound();

        var resolution = await _policies.ResolveAsync(tenantId, retrieval.SourceEntryId, cancellationToken);
        var revision = await _revisions.GetAsync(tenantId, retrieval.Id, cancellationToken)
            ?? new SourceRevisionEvidence(false, false, false);
        var evidence = new SourceFreshnessEvidence(
            SourceFreshnessEvaluator.Canonicalize(
                new DateTimeOffset(DateTime.SpecifyKind(retrieval.RetrievedAtUtc, DateTimeKind.Utc))),
            revision.RevisionVerified, revision.ReplacementProven, revision.ReplacementAuthorityVerified);

        var decision = SourceFreshnessEvaluator.Evaluate(
            resolution?.RegistryPolicy, resolution?.AssertedPolicy, evidence,
            SourceFreshnessEvaluator.Canonicalize(_timeProvider.GetUtcNow()), claimPolicy);
        return new(EvidenceFreshnessOutcome.Evaluated, decision);
    }

    private static EvidenceFreshnessResult NotFound() => new(EvidenceFreshnessOutcome.NotFound, null);
}
