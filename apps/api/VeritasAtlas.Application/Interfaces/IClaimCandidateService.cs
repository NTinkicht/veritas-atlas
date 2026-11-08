using VeritasAtlas.Domain.Parsing;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

public enum ClaimCandidateOutcome
{
    Ok,

    /// <summary>The evidence reference is malformed.</summary>
    InvalidRequest,

    /// <summary>
    /// No verified evidence with this reference exists for the caller's tenant. Unverified evidence,
    /// unknown references and other tenants' references are deliberately indistinguishable.
    /// </summary>
    NotFound,

    /// <summary>Verified chunks failed extraction integrity checks; nothing is returned (fail closed).</summary>
    ExtractionRefused
}

public sealed record ClaimCandidateFreshness(
    FreshnessState State, bool Eligible, string? Reason, string? EvaluatedAt);

public sealed record ClaimCandidateResult(
    ClaimCandidateOutcome Outcome,
    IReadOnlyList<ExtractedClaim> Claims,
    ClaimCandidateFreshness? Freshness);

/// <summary>
/// VER-P0-11 slice 2: read-only, tenant-scoped candidate claims for ONE verified evidence item. Candidates
/// are produced on demand by the deterministic extractor from the stored chunks, are never persisted, are
/// always <see cref="ClaimStatus.Unconfirmed"/> and are never answerable. Freshness is re-evaluated with the
/// server clock on every call so a stale source is labelled rather than silently presented as current.
/// </summary>
public interface IClaimCandidateService
{
    Task<ClaimCandidateResult> ListAsync(Guid tenantId, string evidenceRef, CancellationToken cancellationToken);
}

public static class ClaimCandidateLimits
{
    public const int MaxEvidenceRefLength = 512;
}
