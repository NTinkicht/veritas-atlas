using System.Globalization;
using System.Text.RegularExpressions;

namespace VeritasAtlas.Domain.Provenance;

/// <summary>Exactly one externally visible freshness state (source-freshness/v1).</summary>
public enum FreshnessState
{
    Current,
    Stale,
    Unknown,
    Superseded,
}

/// <summary>
/// Versioned source policy (docs/architecture/SOURCE_FRESHNESS_CONTRACT.md). Every field is
/// nullable so an absent or incomplete policy is representable and classifies Unknown.
/// </summary>
public sealed record SourceFreshnessPolicy(
    string? PolicyId,
    string? PolicyVersion,
    string? SourceMatch,
    bool? FreshnessRequiredForCurrentClaims,
    long? MaxRetrievalAgeSeconds,
    string? RevisionSignal,
    bool? RevisionSignalRequired,
    string? ReplacementAuthority);

/// <summary>Verified inputs for one evidence item; no field is caller opinion about freshness.</summary>
public sealed record SourceFreshnessEvidence(
    string? RetrievedAt,
    bool RevisionVerified,
    bool ReplacementProven,
    bool ReplacementAuthorityVerified);

/// <summary>Claim-level policy; historical use needs both flags (contract rule 8).</summary>
public sealed record SourceFreshnessClaimPolicy(
    bool HistoricalEvidenceAllowed,
    bool HistoricalTimeContextPreserved)
{
    public static readonly SourceFreshnessClaimPolicy CurrentOnly = new(false, false);
}

public static class SourceFreshnessReasons
{
    public const string FreshnessRequiredNonCurrent = "FRESHNESS_REQUIRED_NON_CURRENT";
    public const string HistoricalContextRequired = "HISTORICAL_CONTEXT_REQUIRED";
}

/// <summary>
/// Result of one evaluation. <see cref="EvaluatedAt"/> is always recorded so a decision can be
/// replayed; <see cref="HistoricalAsOf"/> is set only when non-current evidence is allowed under an
/// explicit historical claim policy, and carries the as-of instant of the decision.
/// </summary>
public sealed record SourceFreshnessDecision(
    FreshnessState State,
    bool Eligible,
    string? Reason,
    string? EvaluatedAt,
    long? RetrievalAgeSeconds,
    string? HistoricalAsOf);

/// <summary>
/// Pure, deterministic, fail-closed classifier for <c>source-freshness/v1</c>. No clock, network or
/// I/O: the caller supplies the evaluation instant (the clock-bound entry point is
/// <see cref="SourceFreshnessService"/>). Precedence: Superseded &gt; Unknown &gt; Stale &gt; Current.
/// </summary>
public static class SourceFreshnessEvaluator
{
    private const int MaxFractionDigits = 1000;

    // ASCII digits and an explicit UTC designator only; \z so a trailing newline never matches.
    private static readonly Regex Rfc3339Utc = new(
        @"^(?<whole>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.(?<fraction>[0-9]+))?(?:Z|\+00:00)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Parses a canonical UTC RFC3339 instant to (unix seconds, fraction digits). Fraction digits keep
    /// every accepted digit so no age boundary is rounded away. Returns null when not canonical UTC.
    /// </summary>
    internal static (long Seconds, string Fraction)? ParseUtc(string? value)
    {
        if (value is null) return null;
        Match match;
        try { match = Rfc3339Utc.Match(value); }
        catch (RegexMatchTimeoutException) { return null; }
        if (!match.Success) return null;
        var digits = match.Groups["fraction"].Success ? match.Groups["fraction"].Value : "0";
        if (digits.Length > MaxFractionDigits) return null;
        if (!DateTime.TryParseExact(
                match.Groups["whole"].Value,
                "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var whole))
            return null;
        return (new DateTimeOffset(whole, TimeSpan.Zero).ToUnixTimeSeconds(), digits);
    }

    private static int CompareFraction(string left, string right)
    {
        var width = Math.Max(left.Length, right.Length);
        return string.CompareOrdinal(left.PadRight(width, '0'), right.PadRight(width, '0'));
    }

    private static bool IsComplete(SourceFreshnessPolicy? p) =>
        p is not null
        && !string.IsNullOrEmpty(p.PolicyId)
        && !string.IsNullOrEmpty(p.PolicyVersion)
        && !string.IsNullOrEmpty(p.SourceMatch)
        && p.FreshnessRequiredForCurrentClaims is not null
        && p.MaxRetrievalAgeSeconds is not null
        && !string.IsNullOrEmpty(p.RevisionSignal)
        && p.RevisionSignalRequired is not null
        && !string.IsNullOrEmpty(p.ReplacementAuthority);

    /// <summary>
    /// Classifies one evidence item. <paramref name="registryPolicy"/> is the exact policy selected by
    /// the trusted source registry; <paramref name="assertedPolicy"/> is the policy the evidence was
    /// classified under. Anything but an exact, complete match is unsupported (Unknown): there is no
    /// fallback to a default or previous version.
    /// </summary>
    public static SourceFreshnessDecision Evaluate(
        SourceFreshnessPolicy? registryPolicy,
        SourceFreshnessPolicy? assertedPolicy,
        SourceFreshnessEvidence evidence,
        string? evaluatedAt,
        SourceFreshnessClaimPolicy? claimPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        claimPolicy ??= SourceFreshnessClaimPolicy.CurrentOnly;

        var supported = IsComplete(registryPolicy) && IsComplete(assertedPolicy) && registryPolicy == assertedPolicy;
        long? age = null;
        FreshnessState state;

        if (supported && evidence.ReplacementProven && evidence.ReplacementAuthorityVerified)
        {
            state = FreshnessState.Superseded;
        }
        else
        {
            var retrieved = ParseUtc(evidence.RetrievedAt);
            var evaluated = ParseUtc(evaluatedAt);
            var maxAge = supported ? registryPolicy!.MaxRetrievalAgeSeconds : null;
            var revisionOk = supported
                && (registryPolicy!.RevisionSignalRequired == false || evidence.RevisionVerified);
            var timesOk = retrieved is not null && evaluated is not null
                && (retrieved.Value.Seconds < evaluated.Value.Seconds
                    || (retrieved.Value.Seconds == evaluated.Value.Seconds
                        && CompareFraction(retrieved.Value.Fraction, evaluated.Value.Fraction) <= 0));

            if (!supported || maxAge is null or < 0 || !revisionOk || !timesOk)
            {
                state = FreshnessState.Unknown;
            }
            else
            {
                // Exact floor((evaluated - retrieved) / 1s): whole seconds minus one when the
                // evaluated fraction is smaller. Cannot overflow (both are in DateTime range).
                var seconds = evaluated!.Value.Seconds - retrieved!.Value.Seconds;
                if (CompareFraction(evaluated.Value.Fraction, retrieved.Value.Fraction) < 0) seconds -= 1;
                age = seconds;
                state = seconds <= maxAge ? FreshnessState.Current : FreshnessState.Stale;
            }
        }

        // Freshness requirement comes from the trusted registry; an unusable registry policy
        // is treated as freshness-required (fail closed).
        var freshnessRequired = registryPolicy?.FreshnessRequiredForCurrentClaims != false;
        var historical = claimPolicy.HistoricalEvidenceAllowed && claimPolicy.HistoricalTimeContextPreserved;
        var evaluatedCanonical = ParseUtc(evaluatedAt) is null ? null : evaluatedAt;

        if (state == FreshnessState.Current)
            return new(state, true, null, evaluatedCanonical, age, null);
        if (historical && supported)
        {
            // Historical evidence is returned only with its as-of context (the evaluation instant).
            // Without a valid recorded instant there is no as-of context, so it is not returned.
            return evaluatedCanonical is null
                ? new(state, false, SourceFreshnessReasons.HistoricalContextRequired, null, age, null)
                : new(state, true, null, evaluatedCanonical, age, evaluatedCanonical);
        }
        return new(
            state,
            false,
            freshnessRequired
                ? SourceFreshnessReasons.FreshnessRequiredNonCurrent
                : SourceFreshnessReasons.HistoricalContextRequired,
            evaluatedCanonical,
            age,
            null);
    }

    public static string Canonicalize(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// Application entry point for later search and answer code. The evaluation instant always comes
/// from the injected <see cref="TimeProvider"/>; no overload accepts request-supplied time. Replay
/// of a recorded decision uses <see cref="Replay"/> with that decision's stored instant (rule 10).
/// </summary>
public sealed class SourceFreshnessService
{
    private readonly TimeProvider _time;

    public SourceFreshnessService(TimeProvider time) => _time = time ?? throw new ArgumentNullException(nameof(time));

    public SourceFreshnessDecision Evaluate(
        SourceFreshnessPolicy? registryPolicy,
        SourceFreshnessPolicy? assertedPolicy,
        SourceFreshnessEvidence evidence,
        SourceFreshnessClaimPolicy? claimPolicy = null) =>
        SourceFreshnessEvaluator.Evaluate(
            registryPolicy, assertedPolicy, evidence,
            SourceFreshnessEvaluator.Canonicalize(_time.GetUtcNow()), claimPolicy);

    public static SourceFreshnessDecision Replay(
        SourceFreshnessPolicy? registryPolicy,
        SourceFreshnessPolicy? assertedPolicy,
        SourceFreshnessEvidence evidence,
        string recordedEvaluatedAt,
        SourceFreshnessClaimPolicy? claimPolicy = null) =>
        SourceFreshnessEvaluator.Evaluate(registryPolicy, assertedPolicy, evidence, recordedEvaluatedAt, claimPolicy);
}

/// <summary>One searchable record: id, facet category, retrieval time and its registry policy.</summary>
public sealed record SourceFreshnessQueryRecord(
    string Id,
    string Category,
    SourceFreshnessEvidence Evidence,
    SourceFreshnessPolicy? RegistryPolicy,
    SourceFreshnessPolicy? AssertedPolicy);

public sealed record SourceFreshnessQueryResult(
    IReadOnlyList<string> Ids,
    int Total,
    IReadOnlyDictionary<string, int> Facets,
    IReadOnlyDictionary<string, FreshnessState> States);

/// <summary>
/// <c>source-freshness-query-v1</c> projection: every query/page reclassifies each candidate at this
/// request's trusted instant BEFORE filtering, counting, faceting or paging. Indexed or cached
/// states and earlier-page results are never inputs, so a stale CURRENT badge cannot leak.
/// </summary>
public static class SourceFreshnessQueryProjector
{
    public static SourceFreshnessQueryResult Project(
        IEnumerable<SourceFreshnessQueryRecord> records,
        string? evaluatedAt,
        FreshnessState? filter,
        int pageSize,
        int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (pageSize < 1 || pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var states = new Dictionary<string, FreshnessState>(StringComparer.Ordinal);
        var matching = new List<SourceFreshnessQueryRecord>();
        foreach (var record in records)
        {
            var decision = SourceFreshnessEvaluator.Evaluate(
                record.RegistryPolicy, record.AssertedPolicy, record.Evidence, evaluatedAt);
            states[record.Id] = decision.State;
            if (filter is null || decision.State == filter) matching.Add(record);
        }

        matching.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var facets = matching
            .GroupBy(r => r.Category, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var skip = (long)pageIndex * pageSize;
        var ids = skip >= matching.Count
            ? new List<string>()
            : matching.Skip((int)skip).Take(pageSize).Select(r => r.Id).ToList();
        return new(ids, matching.Count, facets, states);
    }
}
