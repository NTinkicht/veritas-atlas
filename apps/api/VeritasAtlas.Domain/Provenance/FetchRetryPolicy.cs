namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// Why one source fetch attempt failed, as observed by the trusted retriever
/// (VER-P0-06 slice 1, #127).
/// </summary>
public enum FetchRetryFailure
{
    /// <summary>Connection could not be made or was reset before a response.</summary>
    Network,

    /// <summary>The attempt exceeded its time limit.</summary>
    Timeout,

    /// <summary>The body ended early or did not match its declared length (partial bytes).</summary>
    IncompleteResponse,

    /// <summary>The publisher answered with a non-success HTTP status.</summary>
    HttpStatus,

    /// <summary>The response exceeded the admitted size limit.</summary>
    SizeLimit,

    /// <summary>The destination guard or source policy refused the fetch.</summary>
    PolicyDenied
}

/// <summary>
/// Bounded retry schedule for one source fetch. <see cref="MaxAttempts"/> counts every
/// attempt including the first. Values outside the admitted bounds are refused as
/// <c>invalid_schedule</c>, so a misconfigured source can never retry without bound.
/// </summary>
public sealed record SourceFetchRetrySchedule(
    int MaxAttempts,
    TimeSpan BaseDelay,
    TimeSpan MaxDelay,
    TimeSpan MaxRetryAfter)
{
    public const int MinAttempts = 1;
    public const int MaxAdmittedAttempts = 5;
    public static readonly TimeSpan MinBaseDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxAdmittedBaseDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxAdmittedDelay = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxAdmittedRetryAfter = TimeSpan.FromHours(1);

    /// <summary>3 attempts in total, 2 s base, 60 s cap, 300 s Retry-After ceiling.</summary>
    public static readonly SourceFetchRetrySchedule Default = new(
        3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300));
}

/// <summary>
/// What the retriever observed for the attempt that just failed.
/// <paramref name="AttemptNumber"/> is 1-based. <paramref name="HttpStatus"/> is required
/// for <see cref="FetchRetryFailure.HttpStatus"/> and forbidden otherwise.
/// <paramref name="RetryAfter"/> is the publisher's parsed <c>Retry-After</c> delay, if any.
/// </summary>
public sealed record FetchRetryObservation(
    int AttemptNumber,
    FetchRetryFailure Failure,
    int? HttpStatus,
    TimeSpan? RetryAfter);

/// <summary>
/// <see cref="Retry"/> true means: make the next attempt after exactly <see cref="Delay"/>.
/// Otherwise <see cref="Reason"/> is a stable code and no further attempt may be made.
/// </summary>
public sealed record FetchRetryDecision(bool Retry, TimeSpan Delay, string? Reason)
{
    public static FetchRetryDecision After(TimeSpan delay) => new(true, delay, null);
    public static FetchRetryDecision Stop(string reason) => new(false, TimeSpan.Zero, reason);
}

/// <summary>
/// Pure, deterministic, fail-closed retry decision for a failed source fetch. No clock,
/// network, randomness or I/O. Only transient failures are retried: network errors,
/// timeouts, incomplete responses and HTTP 408/429/500/502/503/504. Delay is
/// <c>BaseDelay × 2^(attempt−1)</c> capped at <c>MaxDelay</c>; on 429/503 a
/// <c>Retry-After</c> raises it to at least that value, and one above
/// <c>MaxRetryAfter</c> stops retrying instead of waiting. The first matching rule wins,
/// in this fixed order: <c>invalid_schedule</c>, <c>invalid_observation</c>,
/// <c>policy_denied_not_retryable</c>, <c>size_limit_not_retryable</c>,
/// <c>http_status_not_retryable</c>, <c>attempts_exhausted</c>,
/// <c>retry_after_exceeds_limit</c>.
/// </summary>
public static class FetchRetryPolicy
{
    private static readonly HashSet<int> RetryableStatuses = [408, 429, 500, 502, 503, 504];
    private static readonly HashSet<int> RetryAfterStatuses = [429, 503];

    public static FetchRetryDecision Decide(
        SourceFetchRetrySchedule? schedule,
        FetchRetryObservation? observation)
    {
        if (!IsValid(schedule)) return FetchRetryDecision.Stop("invalid_schedule");
        if (!IsValid(observation, schedule!)) return FetchRetryDecision.Stop("invalid_observation");

        var failure = observation!.Failure;
        if (failure == FetchRetryFailure.PolicyDenied)
            return FetchRetryDecision.Stop("policy_denied_not_retryable");
        if (failure == FetchRetryFailure.SizeLimit)
            return FetchRetryDecision.Stop("size_limit_not_retryable");
        if (failure == FetchRetryFailure.HttpStatus && !RetryableStatuses.Contains(observation.HttpStatus!.Value))
            return FetchRetryDecision.Stop("http_status_not_retryable");
        if (observation.AttemptNumber >= schedule!.MaxAttempts)
            return FetchRetryDecision.Stop("attempts_exhausted");

        var delay = Backoff(schedule, observation.AttemptNumber);
        if (failure == FetchRetryFailure.HttpStatus
            && RetryAfterStatuses.Contains(observation.HttpStatus!.Value)
            && observation.RetryAfter is { } retryAfter)
        {
            if (retryAfter > schedule.MaxRetryAfter)
                return FetchRetryDecision.Stop("retry_after_exceeds_limit");
            if (retryAfter > delay) delay = retryAfter;
        }

        return FetchRetryDecision.After(delay);
    }

    private static TimeSpan Backoff(SourceFetchRetrySchedule schedule, int attemptNumber)
    {
        // attemptNumber < MaxAttempts <= 5, so the shift stays tiny; the cap still applies.
        var ticks = schedule.BaseDelay.Ticks * (1L << (attemptNumber - 1));
        return ticks >= schedule.MaxDelay.Ticks ? schedule.MaxDelay : TimeSpan.FromTicks(ticks);
    }

    private static bool IsValid(SourceFetchRetrySchedule? schedule) =>
        schedule is not null
        && schedule.MaxAttempts >= SourceFetchRetrySchedule.MinAttempts
        && schedule.MaxAttempts <= SourceFetchRetrySchedule.MaxAdmittedAttempts
        && schedule.BaseDelay >= SourceFetchRetrySchedule.MinBaseDelay
        && schedule.BaseDelay <= SourceFetchRetrySchedule.MaxAdmittedBaseDelay
        && schedule.MaxDelay >= schedule.BaseDelay
        && schedule.MaxDelay <= SourceFetchRetrySchedule.MaxAdmittedDelay
        && schedule.MaxRetryAfter >= TimeSpan.Zero
        && schedule.MaxRetryAfter <= SourceFetchRetrySchedule.MaxAdmittedRetryAfter;

    private static bool IsValid(FetchRetryObservation? observation, SourceFetchRetrySchedule schedule)
    {
        if (observation is null) return false;
        if (observation.AttemptNumber < 1 || observation.AttemptNumber > schedule.MaxAttempts) return false;
        if (!Enum.IsDefined(observation.Failure)) return false;
        if (observation.RetryAfter is { } retryAfter && retryAfter < TimeSpan.Zero) return false;
        return observation.Failure == FetchRetryFailure.HttpStatus
            // A 2xx is a success, never a failed attempt.
            ? observation.HttpStatus is (>= 100 and < 200) or (>= 300 and <= 599)
            : observation.HttpStatus is null;
    }
}
