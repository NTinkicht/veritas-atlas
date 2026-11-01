namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// VER-P0-06 slice 12 (#149): pure composition of a failed HTTP attempt into the
/// <see cref="FetchRetryObservation"/> consumed by <see cref="FetchRetryPolicy"/>.
/// The publisher's <c>Retry-After</c> header is untrusted: it is parsed only for the
/// statuses where the policy honours it (429, 503), and an absent or malformed header
/// leaves <c>RetryAfter</c> null so normal bounded backoff applies.
/// </summary>
public static class FetchRetryObservationFactory
{
    /// <returns>
    /// The observation, or <c>null</c> (fail closed: the caller must not retry) for a
    /// non-positive attempt number, a 2xx success, or a status outside 100-599.
    /// </returns>
    public static FetchRetryObservation? FromHttpResponse(
        int attemptNumber,
        int httpStatus,
        string? rawRetryAfter,
        DateTimeOffset trustedNow)
    {
        if (attemptNumber < 1) return null;
        if (httpStatus is < 100 or > 599 or (>= 200 and < 300)) return null;

        TimeSpan? retryAfter = null;
        if (httpStatus is 429 or 503)
        {
            var parsed = RetryAfterHeader.Parse(rawRetryAfter, trustedNow);
            if (parsed.Valid) retryAfter = parsed.Delay;
        }

        return new FetchRetryObservation(attemptNumber, FetchRetryFailure.HttpStatus, httpStatus, retryAfter);
    }

    /// <summary>
    /// VER-P0-06 slice 13 (#151): observation for a failed attempt that produced no HTTP
    /// status (network, timeout, partial bytes, size limit, policy denial).
    /// </summary>
    /// <returns>
    /// The observation, or <c>null</c> (fail closed: the caller must not retry) for a
    /// non-positive attempt number, an undefined failure, or <see cref="FetchRetryFailure.HttpStatus"/>,
    /// which must be built with <see cref="FromHttpResponse"/>.
    /// </returns>
    public static FetchRetryObservation? FromTransportFailure(int attemptNumber, FetchRetryFailure failure)
    {
        if (attemptNumber < 1) return null;
        if (!Enum.IsDefined(failure) || failure == FetchRetryFailure.HttpStatus) return null;
        return new FetchRetryObservation(attemptNumber, failure, null, null);
    }
}
