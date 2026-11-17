namespace VeritasAtlas.Domain.Provenance;

/// <summary>Server-decided public-reader facts a publisher may set (VER-P0-16 slice 7, #202).</summary>
public sealed record PublicVisibilityFactsInput(
    bool Restricted, bool Superseded, DateTimeOffset? ExpiresAt, bool FreshnessRequired,
    EvidenceCurrency EvidenceCurrency, bool HistoricalLabelPresent, IReadOnlyList<PublicCitationProjection> Citations);

public static class VisibilityDenials
{
    public const string InvalidFacts = "invalid_visibility_facts";
    public const string NotSettableInState = "visibility_not_settable_in_state";
    public const string PublicDisplayNotPermitted = "public_display_not_permitted";
    public const string CitationsRequired = "citations_required";
}

/// <summary>
/// Pure validation of publisher-set visibility facts. Facts may only be set while the revision is Approved or Published;
/// every other state keeps (or is reset to) the restrictive defaults. Unrestricting needs the immutable policy permit and at
/// least one well-formed public citation. Fail closed on anything malformed.
/// </summary>
public static class PublicVisibilityRules
{
    public const int MaxCitations = 50;

    /// <summary>Ledger namespace reserved for audited Resets; publisher-supplied operation ids may not start with it.</summary>
    public const string ResetOperationPrefix = "transition:";
    private const int MaxLabel = 100;
    private const int MaxTitle = 300;
    private const int MaxQuote = 2000;
    private const int MaxSource = 200;
    public const int MaxDisclosure = 500;
    public const int MaxSourceUri = 2048;

    /// <summary>
    /// A publisher-supplied provenance link shown to anonymous readers: an absolute https URI with a host, no embedded
    /// credentials, no whitespace or control characters. Anything else is refused so the reader never renders a risky link.
    /// </summary>
    public static bool IsPublicSourceUri(string? value) =>
        value is { Length: > 0 and <= MaxSourceUri }
        && !value.Any(ch => char.IsControl(ch) || char.IsWhiteSpace(ch))
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Host.Length > 0;

    /// <summary>
    /// A public confidence label is exactly one <see cref="ConfidenceBand"/> name (case-sensitive). Numeric text such as "3" is
    /// rejected so no falsely precise or undefined value can reach the anonymous reader.
    /// </summary>
    public static bool IsPublicConfidence(string? value) =>
        value is not null && Enum.GetNames<ConfidenceBand>().Contains(value, StringComparer.Ordinal);

    public static string? Validate(PublicVisibilityFactsInput facts, bool policyPermitsPublicDisplay, LifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (state is not (LifecycleState.Approved or LifecycleState.Published)) return VisibilityDenials.NotSettableInState;
        if (!Enum.IsDefined(facts.EvidenceCurrency) || facts.Citations is null || facts.Citations.Count > MaxCitations)
        {
            return VisibilityDenials.InvalidFacts;
        }

        foreach (var c in facts.Citations)
        {
            if (c is null
                || !Text(c.CitationLabel, MaxLabel, multiline: false) || !Text(c.Title, MaxTitle, multiline: false)
                || !Text(c.Quote, MaxQuote, multiline: true) || !Text(c.SourceName, MaxSource, multiline: false)
                || !Text(c.FreshnessLabel, MaxLabel, multiline: false)
                || (c.ContradictionDisclosure is not null && !Text(c.ContradictionDisclosure, MaxDisclosure, multiline: true))
                || (c.UncertaintyLimit is not null && !Text(c.UncertaintyLimit, MaxDisclosure, multiline: true))
                || (c.SourceUri is not null && !IsPublicSourceUri(c.SourceUri))
                || (c.Certainty is not null && !IsPublicConfidence(c.Certainty)))
            {
                return VisibilityDenials.InvalidFacts;
            }
        }

        if (!facts.Restricted)
        {
            if (!policyPermitsPublicDisplay) return VisibilityDenials.PublicDisplayNotPermitted;
            if (facts.Citations.Count == 0) return VisibilityDenials.CitationsRequired;
        }

        return null;
    }

    private static bool Text(string? value, int max, bool multiline) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max
        && !value.Any(ch => char.IsControl(ch) && !(multiline && ch is '\n' or '\r' or '\t'));
}
