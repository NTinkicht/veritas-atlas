using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-12 (#182) slice 1: validates a claim's cited evidence against the
/// <c>citation-integrity/v1</c> matrix. Every item must independently validate. Freshness is
/// evaluated at validation time through <see cref="IEvidenceFreshnessService"/> (the server clock),
/// never from a cached flag; the parser identity compared is the one bound into the evidence's own
/// parse run, never the currently deployed parser. Read-only: nothing is written or repaired.
/// </summary>
public sealed class EfClaimEvidenceValidationService : IClaimEvidenceValidationService
{
    private static readonly Regex Ref = new(@"^ev1:[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

    private readonly VeritasAtlasDbContext _db;
    private readonly IEvidenceFreshnessService _freshness;

    public EfClaimEvidenceValidationService(VeritasAtlasDbContext db, IEvidenceFreshnessService freshness)
    {
        _db = db;
        _freshness = freshness;
    }

    public async Task<ClaimEvidenceValidationResult> ValidateAsync(
        Guid tenantId, ClaimEvidenceValidationRequest request, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (request?.Citations is not { } citations || citations.Count > ClaimEvidenceLimits.MaxCitations
            || citations.Any(c => c is null || !Bounded(c.ParserName) || !Bounded(c.ParserVersion)))
            return Invalid();

        if (citations.Count == 0)
        {
            return request.EvidenceRequired
                ? new(ClaimEvidenceValidationOutcome.Validated, false, ClaimEvidenceCodes.Required, [])
                : new(ClaimEvidenceValidationOutcome.Validated, true, null, []);
        }

        var items = new List<ClaimEvidenceItemResult>(citations.Count);
        foreach (var citation in citations)
            items.Add(await ValidateItemAsync(tenantId, citation, request.Policy, cancellationToken));

        var firstFailure = items.FirstOrDefault(i => !i.Valid);
        return new(ClaimEvidenceValidationOutcome.Validated, firstFailure is null, firstFailure?.Code, items);
    }

    private async Task<ClaimEvidenceItemResult> ValidateItemAsync(
        Guid tenantId, ClaimEvidenceCitation citation, SourceFreshnessClaimPolicy? policy, CancellationToken ct)
    {
        var evidenceRef = citation.EvidenceRef ?? string.Empty;
        if (!Ref.IsMatch(evidenceRef)) return Reject(evidenceRef, ClaimEvidenceCodes.NotFound);

        // Same candidate rule as the freshness service: the most recently retrieved verified
        // retrieval of this reference, restricted to the caller's tenant.
        var rows = await (
            from v in _db.TenantEvidenceVerifications.AsNoTracking()
            join r in _db.TenantRawRetrievals.AsNoTracking()
                on new { v.TenantId, Id = v.RawRetrievalId } equals new { r.TenantId, r.Id }
            join p in _db.TenantParseRuns.AsNoTracking()
                on new { v.TenantId, Id = v.ParseRunId } equals new { p.TenantId, p.Id }
            where v.TenantId == tenantId && v.ParentEvidenceRef == evidenceRef
            orderby r.RetrievedAtUtc descending, r.Id
            select new
            {
                v.AttestationId, r.SourceEntryId, r.RawBytes, r.ContentSha256,
                p.ParserName, p.ParserVersion,
            }).Take(1).ToListAsync(ct);
        if (rows.Count == 0) return Reject(evidenceRef, ClaimEvidenceCodes.NotFound);
        var row = rows[0];

        var attestation = await _db.TenantRetrievalAttestations.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Id == row.AttestationId)
            .Select(a => a.AuthenticationResult).SingleOrDefaultAsync(ct);
        if (!string.Equals(attestation, "authenticated", StringComparison.Ordinal))
            return Reject(evidenceRef, ClaimEvidenceCodes.ProvenanceIncomplete);

        if (!RawContentDigest.Verify(row.RawBytes, row.ContentSha256).Matches)
            return Reject(evidenceRef, ClaimEvidenceCodes.DigestMismatch);

        if (citation.ScopeSourceEntryId is { } scope && scope != row.SourceEntryId)
            return Reject(evidenceRef, ClaimEvidenceCodes.ScopeMismatch);

        if (!string.Equals(citation.ParserName, row.ParserName, StringComparison.Ordinal)
            || !string.Equals(citation.ParserVersion, row.ParserVersion, StringComparison.Ordinal))
            return Reject(evidenceRef, ClaimEvidenceCodes.ParserMismatch);

        var freshness = await _freshness.EvaluateAsync(tenantId, evidenceRef, policy, ct);
        // The evidence exists in this tenant but its freshness could not be evaluated: fail closed as unknown.
        if (freshness.Outcome != EvidenceFreshnessOutcome.Evaluated || freshness.Decision is not { } decision)
            return Reject(evidenceRef, ClaimEvidenceCodes.FreshnessUnknown);

        if (!decision.Eligible)
        {
            var code = decision.State == FreshnessState.Unknown
                ? ClaimEvidenceCodes.FreshnessUnknown
                : ClaimEvidenceCodes.Stale;
            return new(evidenceRef, false, code, decision.State, decision.EvaluatedAt, null);
        }

        // Non-current evidence is eligible only under an explicit historical policy and then carries
        // its retained freshness state and as-of date (SourceFreshnessEvaluator); it is never labelled current.
        return new(evidenceRef, true, null, decision.State, decision.EvaluatedAt, decision.HistoricalAsOf);
    }

    private static bool Bounded(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= ClaimEvidenceLimits.MaxTextLength && !text.Any(char.IsControl);

    private static ClaimEvidenceItemResult Reject(string evidenceRef, string code) =>
        new(evidenceRef.Length <= 512 && !evidenceRef.Any(char.IsControl) ? evidenceRef : string.Empty,
            false, code, null, null, null);

    private static ClaimEvidenceValidationResult Invalid() =>
        new(ClaimEvidenceValidationOutcome.InvalidRequest, false, null, []);
}
