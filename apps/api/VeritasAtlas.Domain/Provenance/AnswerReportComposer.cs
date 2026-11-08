namespace VeritasAtlas.Domain.Provenance;

/// <summary>A citation as shown in a composed report: the immutable evidence reference and its serve-time freshness label.</summary>
public sealed record ReportCitation(string EvidenceRef, string Freshness);

/// <summary>
/// A claim as shown in a composed report. <see cref="TemporalScope"/> is "current" or "historical"; a historical
/// claim always carries its <see cref="HistoricalAsOf"/> date and <see cref="NonCurrentEvidence"/> tells the
/// reader that at least one citation is not Current, so it cannot be mistaken for a current-state assertion.
/// </summary>
public sealed record ReportClaim(
    string ClaimId, string Text, string TemporalScope, DateOnly? HistoricalAsOf, bool NonCurrentEvidence,
    IReadOnlyList<ReportCitation> Citations);

/// <summary>A contradiction disclosure restricted to the claims that are actually in the report.</summary>
public sealed record ReportContradictionDisclosure(string ContradictionId, IReadOnlyList<string> ClaimIds);

public sealed record ComposedReport(
    string HumanVerification, IReadOnlyList<ReportClaim> Claims, IReadOnlyList<ReportContradictionDisclosure> Contradictions);

public sealed record ReportComposition(ServeResult Verdict, ComposedReport? Report);

/// <summary>
/// Pure deterministic source-backed report composer (VER-P0-15 slice 3). It runs the fail-closed
/// <see cref="AnswerServeGate"/> first and produces a report only on <see cref="ServeDecision.Serve"/>, so a
/// blocked answer never yields claim text. Claim text is carried through verbatim and never interpreted. No
/// clock, network or I/O: freshness must already have been re-evaluated by the caller immediately before serving.
/// </summary>
public static class AnswerReportComposer
{
    public static ReportComposition Compose(
        string tenantId, IReadOnlyList<ServeClaim>? claims, IReadOnlyDictionary<string, string>? claimTexts,
        IReadOnlyList<KnownContradiction>? knownContradictions, IReadOnlySet<string>? disclosedContradictionIds,
        HumanVerificationState verification)
    {
        var verdict = AnswerServeGate.Evaluate(tenantId, claims, knownContradictions, disclosedContradictionIds, verification);
        if (verdict.Decision != ServeDecision.Serve || claims is null) return new(verdict, null);

        // A served claim must have text to show. Missing or blank text fails closed rather than rendering an empty claim.
        var blockers = new List<ServeBlocker>();
        var reportClaims = new List<ReportClaim>(claims.Count);
        foreach (var c in claims.OrderBy(x => x.ClaimId, StringComparer.Ordinal))
        {
            if (claimTexts is null || !claimTexts.TryGetValue(c.ClaimId, out var text) || string.IsNullOrWhiteSpace(text))
            {
                blockers.Add(new(ServeBlockers.ClaimTextMissing, c.ClaimId, null));
                continue;
            }

            // The gate only demands an as-of date when a citation is non-current. A historical claim must always
            // disclose its as-of date, even if every citation happens to be Current, so it cannot read as current-state.
            if (!c.CurrentState && c.HistoricalAsOf is null)
            {
                blockers.Add(new(ServeBlockers.HistoricalAsOfMissing, c.ClaimId, null));
                continue;
            }

            var citations = c.Citations!
                .OrderBy(x => x.EvidenceRef, StringComparer.Ordinal)
                .Select(x => new ReportCitation(x.EvidenceRef, (x.ServeTimeFreshness ?? FreshnessState.Unknown).ToString().ToLowerInvariant()))
                .ToList();
            var nonCurrent = c.Citations!.Any(x => (x.ServeTimeFreshness ?? FreshnessState.Unknown) != FreshnessState.Current);
            reportClaims.Add(new(
                c.ClaimId, text, c.CurrentState ? "current" : "historical", c.CurrentState ? null : c.HistoricalAsOf, nonCurrent, citations));
        }

        if (blockers.Count > 0) return new(new(ServeDecision.Block, blockers), null);

        var ids = reportClaims.Select(x => x.ClaimId).ToHashSet(StringComparer.Ordinal);
        var contradictions = (knownContradictions ?? [])
            .Select(k => new ReportContradictionDisclosure(
                k.ContradictionId, (k.ClaimIds ?? []).Where(ids.Contains).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()))
            .Where(k => k.ClaimIds.Count > 0)
            .OrderBy(k => k.ContradictionId, StringComparer.Ordinal)
            .ToList();
        return new(verdict, new("verified", reportClaims, contradictions));
    }
}
