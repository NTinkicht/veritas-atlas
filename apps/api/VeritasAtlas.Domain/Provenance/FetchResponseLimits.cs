namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// Response size and time limits for one independent server-side source fetch
/// (VER-P0-04 slice 3, #115). Limits outside the admitted bounds are rejected
/// as <c>invalid_limits</c>, so a misconfigured source can never fetch unbounded.
/// </summary>
public sealed record SourceFetchLimits(long MaxResponseBytes, TimeSpan MaxTotalDuration)
{
    public const long MinResponseBytes = 1;
    public const long MaxAdmittedResponseBytes = 64L * 1024 * 1024;
    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxAdmittedDuration = TimeSpan.FromSeconds(120);
}

/// <summary>
/// What the trusted retriever observed for a finished (or aborted) response.
/// <paramref name="DeclaredContentLength"/> is the Content-Length header when present;
/// <paramref name="Complete"/> is true only when the body ended normally.
/// </summary>
public sealed record FetchResponseObservation(
    long? DeclaredContentLength,
    long BytesRead,
    TimeSpan Elapsed,
    bool Complete);

public sealed record FetchLimitDecision(bool Admitted, string? Reason)
{
    public static readonly FetchLimitDecision Admit = new(true, null);
    public static FetchLimitDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Pure, fail-closed size/time limit decisions. No clock, network or stream access:
/// elapsed time and byte counts are explicit inputs from the trusted retriever.
/// Equality with a limit is admitted; one byte or one tick over is denied.
/// The first violated rule wins, in this fixed order: <c>invalid_limits</c>,
/// <c>invalid_observation</c>, <c>content_length_exceeds_limit</c>,
/// <c>response_too_large</c>, <c>fetch_timeout</c>, then (completed responses only)
/// <c>incomplete_response</c> and <c>content_length_mismatch</c>.
/// </summary>
public static class FetchResponseLimitGuard
{
    /// <summary>
    /// Streaming check while the body is still being read: a denial means abort now.
    /// </summary>
    public static FetchLimitDecision CheckProgress(
        SourceFetchLimits? limits,
        long? declaredContentLength,
        long bytesReadSoFar,
        TimeSpan elapsed)
        => CheckBounds(limits, declaredContentLength, bytesReadSoFar, elapsed) is { } reason
            ? FetchLimitDecision.Deny(reason)
            : FetchLimitDecision.Admit;

    /// <summary>
    /// Final decision for a response. Partial bodies and bodies whose length differs
    /// from the declared Content-Length are never admitted.
    /// </summary>
    public static FetchLimitDecision EvaluateCompleted(
        SourceFetchLimits? limits,
        FetchResponseObservation? observation)
    {
        if (!LimitsAreValid(limits)) return FetchLimitDecision.Deny("invalid_limits");
        if (observation is null) return FetchLimitDecision.Deny("invalid_observation");
        var reason = CheckBounds(
            limits,
            observation.DeclaredContentLength,
            observation.BytesRead,
            observation.Elapsed);
        if (reason is not null) return FetchLimitDecision.Deny(reason);
        if (!observation.Complete) return FetchLimitDecision.Deny("incomplete_response");
        if (observation.DeclaredContentLength is { } declared && declared != observation.BytesRead)
            return FetchLimitDecision.Deny("content_length_mismatch");
        return FetchLimitDecision.Admit;
    }

    /// <summary>
    /// Maps a denial reason to the fetch-attempt ledger failure class
    /// (<c>ck_tenant_fetch_attempts_failure_class</c>). Unknown reasons fail closed
    /// to <c>policy_denied</c>, as does an inconsistent decision (admitted with a
    /// reason); only a clean admission has no failure class.
    /// </summary>
    public static string? LedgerFailureClass(FetchLimitDecision? decision)
    {
        if (decision is { Admitted: true, Reason: null }) return null;
        return decision?.Reason switch
        {
            "content_length_exceeds_limit" or "response_too_large" => "size_limit",
            "fetch_timeout" => "timeout",
            _ => "policy_denied",
        };
    }

    private static bool LimitsAreValid(SourceFetchLimits? limits)
        => limits is not null
            && limits.MaxResponseBytes >= SourceFetchLimits.MinResponseBytes
            && limits.MaxResponseBytes <= SourceFetchLimits.MaxAdmittedResponseBytes
            && limits.MaxTotalDuration >= SourceFetchLimits.MinDuration
            && limits.MaxTotalDuration <= SourceFetchLimits.MaxAdmittedDuration;

    private static string? CheckBounds(
        SourceFetchLimits? limits,
        long? declaredContentLength,
        long bytesRead,
        TimeSpan elapsed)
    {
        if (!LimitsAreValid(limits)) return "invalid_limits";
        if (declaredContentLength < 0 || bytesRead < 0 || elapsed < TimeSpan.Zero)
            return "invalid_observation";
        if (declaredContentLength > limits!.MaxResponseBytes) return "content_length_exceeds_limit";
        if (bytesRead > limits.MaxResponseBytes) return "response_too_large";
        if (elapsed > limits.MaxTotalDuration) return "fetch_timeout";
        return null;
    }
}
