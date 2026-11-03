namespace VeritasAtlas.Domain.Provenance;

public enum EvidenceCurrency { Current, NonCurrent, Unknown }

public static class PublicHiddenReasons
{
    public const string NotPublished = "not_published";
    public const string PublicDisplayNotPermitted = "public_display_not_permitted";
    public const string Restricted = "restricted";
    public const string Superseded = "superseded";
    public const string Expired = "expired";
    public const string StaleCurrentState = "stale_current_state";
    public const string HistoricalLabelMissing = "historical_label_missing";
}

/// <summary>Trusted server-side facts about one revision. Unknown or missing facts must be passed as the restrictive value.</summary>
public sealed record PublicReaderFacts(
    LifecycleState State, bool PolicyPermitsPublicDisplay, bool Restricted, bool Superseded,
    DateTimeOffset? ExpiresAt, DateTimeOffset Now, bool FreshnessRequired, EvidenceCurrency EvidenceCurrency,
    bool HistoricalTimeContextLabelPresent);

public sealed record PublicReaderDecision(bool Visible, string? InternalReason)
{
    /// <summary>Every hidden cause maps to the same anonymous-facing outcome so the cause is not inferable.</summary>
    public const string AnonymousHiddenOutcome = "not_found";
}

/// <summary>Allow-listed public citation/evidence fields. Deliberately excludes tenant, internal ids, storage keys, digests and reviewer identity.</summary>
public sealed record PublicCitationProjection(
    string CitationLabel, string Title, string Quote, string SourceName, DateTimeOffset? AsOf,
    DateTimeOffset? EffectiveDate, string FreshnessLabel);

/// <summary>Pure fail-closed public-reader visibility rules (VER-P0-16 slice 2). Approved is never public.</summary>
public static class PublicReaderGate
{
    public static PublicReaderDecision Evaluate(PublicReaderFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (f.State != LifecycleState.Published) return Hide(PublicHiddenReasons.NotPublished);
        if (!f.PolicyPermitsPublicDisplay) return Hide(PublicHiddenReasons.PublicDisplayNotPermitted);
        if (f.Restricted) return Hide(PublicHiddenReasons.Restricted);
        if (f.Superseded) return Hide(PublicHiddenReasons.Superseded);
        if (f.ExpiresAt is { } exp && exp <= f.Now) return Hide(PublicHiddenReasons.Expired);
        if (f.FreshnessRequired)
        {
            if (f.EvidenceCurrency != EvidenceCurrency.Current) return Hide(PublicHiddenReasons.StaleCurrentState);
        }
        else if (f.EvidenceCurrency != EvidenceCurrency.Current && !f.HistoricalTimeContextLabelPresent)
        {
            return Hide(PublicHiddenReasons.HistoricalLabelMissing);
        }
        return new(true, null);
    }

    private static PublicReaderDecision Hide(string reason) => new(false, reason);
}
