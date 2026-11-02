using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// The registry policy selected for one source plus the policy the evidence was classified under.
/// Only a trusted source registry may produce this; <see cref="SourceFreshnessEvaluator"/> treats
/// any mismatch or incompleteness as Unknown.
/// </summary>
public sealed record SourceFreshnessPolicyResolution(
    SourceFreshnessPolicy? RegistryPolicy, SourceFreshnessPolicy? AssertedPolicy);

/// <summary>Verified revision / supersession proof for one raw retrieval (never caller opinion).</summary>
public sealed record SourceRevisionEvidence(
    bool RevisionVerified, bool ReplacementProven, bool ReplacementAuthorityVerified);

/// <summary>
/// Trusted registry boundary for the per-source freshness policy (VER-P0-08, #175). The only
/// production implementation shipped so far answers <c>null</c> (no policy), so freshness is
/// Unknown until a later slice supplies a real, persisted policy selection.
/// </summary>
public interface ISourceFreshnessPolicyResolver
{
    Task<SourceFreshnessPolicyResolution?> ResolveAsync(
        Guid tenantId, Guid sourceEntryId, CancellationToken cancellationToken);
}

/// <summary>Authoritative revision / supersession boundary; nothing is proven by default.</summary>
public interface ISourceRevisionEvidenceProvider
{
    Task<SourceRevisionEvidence> GetAsync(Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken);
}

/// <summary>Fail-closed production policy resolver: no policy for any source.</summary>
public sealed class FailClosedSourceFreshnessPolicyResolver : ISourceFreshnessPolicyResolver
{
    public Task<SourceFreshnessPolicyResolution?> ResolveAsync(
        Guid tenantId, Guid sourceEntryId, CancellationToken cancellationToken) =>
        Task.FromResult<SourceFreshnessPolicyResolution?>(null);
}

/// <summary>Fail-closed production revision provider: nothing verified, nothing proven.</summary>
public sealed class FailClosedSourceRevisionEvidenceProvider : ISourceRevisionEvidenceProvider
{
    public Task<SourceRevisionEvidence> GetAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken) =>
        Task.FromResult(new SourceRevisionEvidence(false, false, false));
}

public enum EvidenceFreshnessOutcome
{
    /// <summary>The evidence is verified and a decision was computed (it may be Unknown/Stale).</summary>
    Evaluated,

    /// <summary>
    /// No verified evidence with this reference exists for the caller's tenant. Unverified evidence,
    /// unknown references and other tenants' references are deliberately indistinguishable.
    /// </summary>
    NotFound
}

public sealed record EvidenceFreshnessResult(EvidenceFreshnessOutcome Outcome, SourceFreshnessDecision? Decision);

/// <summary>
/// VER-P0-08 (#175): tenant-scoped freshness of a verified evidence item. The evaluation instant
/// always comes from the server clock; no overload accepts caller-supplied time.
/// </summary>
public interface IEvidenceFreshnessService
{
    Task<EvidenceFreshnessResult> EvaluateAsync(
        Guid tenantId, string evidenceRef, SourceFreshnessClaimPolicy? claimPolicy, CancellationToken cancellationToken);
}
