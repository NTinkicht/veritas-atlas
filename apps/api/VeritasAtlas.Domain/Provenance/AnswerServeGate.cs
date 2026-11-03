namespace VeritasAtlas.Domain.Provenance;

public enum HumanVerificationState { Unreviewed, Verified, Rejected }

public enum ServeDecision { Serve, Block }

public static class ServeBlockers
{
    public const string TenantMissing = "tenant_missing";
    public const string NoClaims = "no_claims";
    public const string ClaimWithoutCitation = "claim_without_citation";
    public const string CitationBlankRef = "citation_blank_ref";
    public const string CitationDuplicateRef = "citation_duplicate_ref";
    public const string CitationNotVerified = "citation_not_verified";
    public const string CitationCrossTenant = "citation_cross_tenant";
    public const string CurrentStateNotCurrent = "current_state_evidence_not_current";
    public const string HistoricalAsOfMissing = "historical_as_of_missing";
    public const string ContradictionNotDisclosed = "contradiction_not_disclosed";
    public const string HumanVerificationMissing = "human_verification_missing";
}

/// <summary>One citation behind a claim. <see cref="ServeTimeFreshness"/> must be re-evaluated immediately before serving; null means "not evaluated" and is treated as Unknown.</summary>
public sealed record ServeCitation(string TenantId, string EvidenceRef, bool VerifiedAndAnswerable, FreshnessState? ServeTimeFreshness);

/// <summary>A claim in a composed answer. A current-state claim needs Current evidence; a historical claim is allowed only with a visible as-of date.</summary>
public sealed record ServeClaim(string ClaimId, bool CurrentState, DateOnly? HistoricalAsOf, IReadOnlyList<ServeCitation>? Citations);

/// <summary>A known conflicting or unresolved relation (from the claim-relation classifier) that touches the listed claims.</summary>
public sealed record KnownContradiction(string ContradictionId, IReadOnlyList<string> ClaimIds);

public sealed record ServeBlocker(string Code, string? ClaimId, string? Detail);

public sealed record ServeResult(ServeDecision Decision, IReadOnlyList<ServeBlocker> Blockers);

/// <summary>Pure deterministic fail-closed serve gate (VER-P0-15 slice 1). No clock, network or I/O; it only judges supplied facts.</summary>
public static class AnswerServeGate
{
    public static ServeResult Evaluate(
        string tenantId, IReadOnlyList<ServeClaim>? claims,
        IReadOnlyList<KnownContradiction>? knownContradictions, IReadOnlySet<string>? disclosedContradictionIds,
        HumanVerificationState verification)
    {
        var b = new List<ServeBlocker>();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            b.Add(new(ServeBlockers.TenantMissing, null, null));
            return new(ServeDecision.Block, b);
        }
        if (claims is null || claims.Count == 0) b.Add(new(ServeBlockers.NoClaims, null, null));

        foreach (var c in (claims ?? []).OrderBy(x => x.ClaimId, StringComparer.Ordinal))
        {
            if (c.Citations is null || c.Citations.Count == 0) { b.Add(new(ServeBlockers.ClaimWithoutCitation, c.ClaimId, null)); continue; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ct in c.Citations)
            {
                if (string.IsNullOrWhiteSpace(ct.EvidenceRef)) { b.Add(new(ServeBlockers.CitationBlankRef, c.ClaimId, null)); continue; }
                if (!seen.Add(ct.EvidenceRef)) b.Add(new(ServeBlockers.CitationDuplicateRef, c.ClaimId, ct.EvidenceRef));
                if (!string.Equals(ct.TenantId, tenantId, StringComparison.Ordinal)) b.Add(new(ServeBlockers.CitationCrossTenant, c.ClaimId, ct.EvidenceRef));
                if (!ct.VerifiedAndAnswerable) b.Add(new(ServeBlockers.CitationNotVerified, c.ClaimId, ct.EvidenceRef));
                var f = ct.ServeTimeFreshness ?? FreshnessState.Unknown;
                if (f == FreshnessState.Current) continue;
                if (c.CurrentState) b.Add(new(ServeBlockers.CurrentStateNotCurrent, c.ClaimId, ct.EvidenceRef));
                else if (c.HistoricalAsOf is null) b.Add(new(ServeBlockers.HistoricalAsOfMissing, c.ClaimId, ct.EvidenceRef));
            }
        }

        var ids = claims?.Select(x => x.ClaimId).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var k in (knownContradictions ?? []).OrderBy(x => x.ContradictionId, StringComparer.Ordinal))
        {
            var touches = k.ClaimIds is not null && k.ClaimIds.Any(ids.Contains);
            if (touches && disclosedContradictionIds?.Contains(k.ContradictionId) != true)
                b.Add(new(ServeBlockers.ContradictionNotDisclosed, null, k.ContradictionId));
        }

        if (verification != HumanVerificationState.Verified) b.Add(new(ServeBlockers.HumanVerificationMissing, null, verification.ToString()));
        return new(b.Count == 0 ? ServeDecision.Serve : ServeDecision.Block, b);
    }
}
