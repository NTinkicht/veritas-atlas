using System.Globalization;

namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// Result of parsing a publisher <c>Retry-After</c> header (VER-P0-06 slice 11, #147).
/// <see cref="Valid"/> false means the header must be treated as absent (normal backoff applies).
/// </summary>
public sealed record RetryAfterParse(bool Valid, TimeSpan Delay, string? Reason)
{
    public static RetryAfterParse Ok(TimeSpan delay) => new(true, delay, null);
    public static RetryAfterParse Invalid(string reason) => new(false, TimeSpan.Zero, reason);
}

/// <summary>
/// Pure, deterministic, fail-closed parser for an untrusted <c>Retry-After</c> value. No clock,
/// network or I/O: the caller supplies the trusted evaluation instant. Accepts delta-seconds
/// (ASCII digits only) or an IMF-fixdate HTTP-date ending in <c>GMT</c>. A date in the past
/// yields zero delay. Well-formed values above 24 h are clamped to 24 h, so
/// <see cref="FetchRetryPolicy"/> stops with <c>retry_after_exceeds_limit</c> and never retries
/// early. Anything else is <c>Valid=false</c> with a stable reason: <c>missing</c>,
/// <c>too_long</c>, <c>malformed</c>, <c>invalid_now</c>.
/// </summary>
public static class RetryAfterHeader
{
    public static readonly TimeSpan MaxParsedDelay = TimeSpan.FromHours(24);
    private const int MaxInputLength = 64;
    private const string ImfFixdate = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";

    public static RetryAfterParse Parse(string? value, DateTimeOffset trustedNow)
    {
        if (trustedNow == default) return RetryAfterParse.Invalid("invalid_now");
        if (string.IsNullOrWhiteSpace(value)) return RetryAfterParse.Invalid("missing");
        if (value.Length > MaxInputLength) return RetryAfterParse.Invalid("too_long");

        var text = value.Trim(' ', '\t');
        if (text.Length > 0 && text.All(static c => c is >= '0' and <= '9'))
        {
            if (text.Length > 9) return RetryAfterParse.Ok(MaxParsedDelay);
            var seconds = long.Parse(text, CultureInfo.InvariantCulture);
            return RetryAfterParse.Ok(Clamp(TimeSpan.FromSeconds(seconds)));
        }

        if (text.Length > 0 && text.All(static c => c < 128)
            && DateTimeOffset.TryParseExact(text, ImfFixdate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
        {
            var delta = when - trustedNow;
            return RetryAfterParse.Ok(delta <= TimeSpan.Zero ? TimeSpan.Zero : Clamp(delta));
        }

        return RetryAfterParse.Invalid("malformed");
    }

    private static TimeSpan Clamp(TimeSpan delay) => delay > MaxParsedDelay ? MaxParsedDelay : delay;
}
