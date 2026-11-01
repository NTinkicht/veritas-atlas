namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// VER-P0-06 slice 14 (#153): pure conversion of a <see cref="FetchRetryDecision"/> into
/// the absolute, trusted-clock time of the next attempt. No clock, I/O or persistence.
/// </summary>
public static class FetchRetryDueTime
{
    private static readonly TimeSpan MaxDelay =
        SourceFetchRetrySchedule.MaxAdmittedDelay > SourceFetchRetrySchedule.MaxAdmittedRetryAfter
            ? SourceFetchRetrySchedule.MaxAdmittedDelay
            : SourceFetchRetrySchedule.MaxAdmittedRetryAfter;

    /// <returns>
    /// <c>trustedNow + Delay</c>, or <c>null</c> (fail closed: do not retry) for a null or
    /// stop decision, a retry carrying a stop reason, a negative or above-bound delay, or
    /// a sum that would overflow.
    /// </returns>
    public static DateTimeOffset? From(DateTimeOffset trustedNow, FetchRetryDecision? decision)
    {
        if (decision is null || !decision.Retry || decision.Reason is not null) return null;
        if (decision.Delay < TimeSpan.Zero || decision.Delay > MaxDelay) return null;
        if (trustedNow > DateTimeOffset.MaxValue - decision.Delay) return null;
        return trustedNow + decision.Delay;
    }
}
