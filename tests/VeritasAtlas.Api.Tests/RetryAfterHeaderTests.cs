using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-06 slice 11 (#147): fail-closed Retry-After parsing vectors.</summary>
[TestClass]
public sealed class RetryAfterHeaderTests
{
    private static readonly DateTimeOffset Now = new(1994, 11, 6, 8, 49, 37, TimeSpan.Zero);

    [TestMethod]
    public void Delta_seconds_parse_exactly_and_trim_spaces()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(120), RetryAfterHeader.Parse("120", Now).Delay);
        Assert.AreEqual(TimeSpan.Zero, RetryAfterHeader.Parse("0", Now).Delay);
        Assert.IsTrue(RetryAfterHeader.Parse("  7\t", Now).Valid);
        Assert.AreEqual(TimeSpan.FromSeconds(7), RetryAfterHeader.Parse("  7\t", Now).Delay);
    }

    [TestMethod]
    public void Zero_padded_delta_seconds_use_their_numeric_value()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(30), RetryAfterHeader.Parse("0000000030", Now).Delay);
        Assert.AreEqual(TimeSpan.Zero, RetryAfterHeader.Parse("0000000000000", Now).Delay);
        Assert.AreEqual(RetryAfterHeader.MaxParsedDelay, RetryAfterHeader.Parse("00001000000000", Now).Delay);
        var d = FetchRetryPolicy.Decide(SourceFetchRetrySchedule.Default,
            new FetchRetryObservation(1, FetchRetryFailure.HttpStatus, 429, RetryAfterHeader.Parse("0000000030", Now).Delay));
        Assert.IsTrue(d.Retry);
    }

    [TestMethod]
    public void Http_date_is_relative_to_the_trusted_now()
    {
        var r = RetryAfterHeader.Parse("Sun, 06 Nov 1994 08:51:37 GMT", Now);
        Assert.IsTrue(r.Valid);
        Assert.AreEqual(TimeSpan.FromMinutes(2), r.Delay);
        // Offset-bearing trusted now is normalised to the same instant.
        var local = new DateTimeOffset(1994, 11, 6, 12, 49, 37, TimeSpan.FromHours(4));
        Assert.AreEqual(TimeSpan.FromMinutes(2), RetryAfterHeader.Parse("Sun, 06 Nov 1994 08:51:37 GMT", local).Delay);
    }

    [TestMethod]
    public void Past_or_equal_date_is_zero_delay()
    {
        Assert.AreEqual(TimeSpan.Zero, RetryAfterHeader.Parse("Sun, 06 Nov 1994 08:49:37 GMT", Now).Delay);
        Assert.AreEqual(TimeSpan.Zero, RetryAfterHeader.Parse("Sun, 06 Nov 1994 08:00:00 GMT", Now).Delay);
    }

    [TestMethod]
    public void Large_wellformed_values_clamp_to_24h_so_the_policy_stops()
    {
        Assert.AreEqual(RetryAfterHeader.MaxParsedDelay, RetryAfterHeader.Parse("999999999", Now).Delay);
        Assert.AreEqual(RetryAfterHeader.MaxParsedDelay, RetryAfterHeader.Parse("99999999999999999999", Now).Delay);
        Assert.AreEqual(RetryAfterHeader.MaxParsedDelay, RetryAfterHeader.Parse("Fri, 31 Dec 9999 23:59:59 GMT", Now).Delay);
        var schedule = SourceFetchRetrySchedule.Default;
        var parsed = RetryAfterHeader.Parse("86400", Now);
        var decision = FetchRetryPolicy.Decide(schedule,
            new FetchRetryObservation(1, FetchRetryFailure.HttpStatus, 429, parsed.Delay));
        Assert.IsFalse(decision.Retry);
        Assert.AreEqual("retry_after_exceeds_limit", decision.Reason);
    }

    [TestMethod]
    public void Parsed_value_composes_with_the_retry_policy()
    {
        var parsed = RetryAfterHeader.Parse("30", Now);
        var decision = FetchRetryPolicy.Decide(SourceFetchRetrySchedule.Default,
            new FetchRetryObservation(1, FetchRetryFailure.HttpStatus, 503, parsed.Delay));
        Assert.IsTrue(decision.Retry);
        Assert.AreEqual(TimeSpan.FromSeconds(30), decision.Delay);
    }

    [DataTestMethod]
    [DataRow("-5")]
    [DataRow("+5")]
    [DataRow("1.5")]
    [DataRow("1e3")]
    [DataRow("0x10")]
    [DataRow("١٢")]
    [DataRow("5 seconds")]
    [DataRow("Sunday, 06-Nov-94 08:49:37 GMT")]
    [DataRow("Sun Nov  6 08:49:37 1994")]
    [DataRow("Sun, 06 Nov 1994 08:49:37 UTC")]
    [DataRow("Sun, 06 Nov 1994 08:49:37 +0000")]
    [DataRow("Mon, 06 Nov 1994 08:49:37 GMT")]
    [DataRow("Sun, 06 Nov 1994 08:49:37")]
    [DataRow("1\n2")]
    public void Malformed_values_are_invalid_and_never_a_delay(string value)
    {
        var r = RetryAfterHeader.Parse(value, Now);
        Assert.IsFalse(r.Valid, value);
        Assert.AreEqual("malformed", r.Reason);
        Assert.AreEqual(TimeSpan.Zero, r.Delay);
    }

    [TestMethod]
    public void Missing_oversized_and_invalid_now_are_refused_with_stable_reasons()
    {
        Assert.AreEqual("missing", RetryAfterHeader.Parse(null, Now).Reason);
        Assert.AreEqual("missing", RetryAfterHeader.Parse("   ", Now).Reason);
        Assert.AreEqual("too_long", RetryAfterHeader.Parse(new string('1', 65), Now).Reason);
        Assert.AreEqual("invalid_now", RetryAfterHeader.Parse("5", default).Reason);
        Assert.IsFalse(RetryAfterHeader.Parse("5", default).Valid);
    }
}
