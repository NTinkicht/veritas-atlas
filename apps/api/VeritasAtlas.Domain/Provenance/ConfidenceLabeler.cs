namespace VeritasAtlas.Domain.Provenance;

public enum ConfidenceBand { Insufficient = 0, Low = 1, Medium = 2, High = 3 }

public static class ConfidenceReasons
{
    public const string InputMissing = "input_missing";
    public const string NoSupport = "no_support";
    public const string ConflictingEvidence = "conflicting_evidence";
    public const string SupersededEvidence = "superseded_evidence";
    public const string StaleEvidence = "stale_evidence";
    public const string UnknownFreshness = "unknown_freshness";
    public const string UnresolvedRelations = "unresolved_relations";
    public const string SingleIndependentSource = "single_independent_source";
    public const string MultipleIndependentSources = "multiple_independent_sources";
}

/// <summary>
/// Quality signals supplied by the caller for one claim. A null signal means "not available" and is never defaulted.
/// </summary>
public sealed record ConfidenceInputs(
    int? SupportingEvidenceCount, int? IndependentSourceCount, int? ConflictingCount,
    int? SupersededCount, int? StaleCount, int? UnknownFreshnessCount, int? UnresolvedRelationCount);

/// <summary>Coarse band plus ordered reasons. It describes evidence quality only; it is not a judgement that the claim is true.</summary>
public sealed record ConfidenceLabel(ConfidenceBand Band, IReadOnlyList<string> Reasons);

/// <summary>Pure deterministic confidence-band labeler (VER-P0-14 slice 1). Fail-closed and monotone: extra negative signals never raise the band.</summary>
public static class ConfidenceLabeler
{
    public static ConfidenceLabel Label(ConfidenceInputs i)
    {
        ArgumentNullException.ThrowIfNull(i);
        foreach (var v in new[] { i.SupportingEvidenceCount, i.IndependentSourceCount, i.ConflictingCount, i.SupersededCount, i.StaleCount, i.UnknownFreshnessCount, i.UnresolvedRelationCount })
            if (v is < 0) throw new ArgumentOutOfRangeException(nameof(i), "Counts must not be negative.");

        if (i.SupportingEvidenceCount is null || i.IndependentSourceCount is null || i.ConflictingCount is null ||
            i.SupersededCount is null || i.StaleCount is null || i.UnknownFreshnessCount is null || i.UnresolvedRelationCount is null)
            return new(ConfidenceBand.Insufficient, [ConfidenceReasons.InputMissing]);
        if (i.SupportingEvidenceCount == 0 || i.IndependentSourceCount == 0)
            return new(ConfidenceBand.Insufficient, [ConfidenceReasons.NoSupport]);

        var reasons = new List<string>();
        var band = ConfidenceBand.High;
        if (i.IndependentSourceCount == 1) { band = ConfidenceBand.Medium; reasons.Add(ConfidenceReasons.SingleIndependentSource); }
        else reasons.Add(ConfidenceReasons.MultipleIndependentSources);

        if (i.ConflictingCount > 0) { band = Min(band, ConfidenceBand.Low); reasons.Add(ConfidenceReasons.ConflictingEvidence); }
        if (i.SupersededCount > 0) { band = Min(band, ConfidenceBand.Low); reasons.Add(ConfidenceReasons.SupersededEvidence); }
        if (i.StaleCount > 0) { band = Down(band); reasons.Add(ConfidenceReasons.StaleEvidence); }
        if (i.UnknownFreshnessCount > 0) { band = Down(band); reasons.Add(ConfidenceReasons.UnknownFreshness); }
        if (i.UnresolvedRelationCount > 0) { band = Down(band); reasons.Add(ConfidenceReasons.UnresolvedRelations); }
        return new(band, reasons);
    }

    private static ConfidenceBand Min(ConfidenceBand a, ConfidenceBand b) => a < b ? a : b;
    // Degradation never goes below Low: Insufficient is reserved for missing inputs or no support.
    private static ConfidenceBand Down(ConfidenceBand b) => b > ConfidenceBand.Low ? b - 1 : ConfidenceBand.Low;
}
