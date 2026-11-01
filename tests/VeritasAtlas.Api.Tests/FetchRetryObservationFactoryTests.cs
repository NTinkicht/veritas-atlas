using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-06 slice 12 (#149): HTTP failure to retry observation vectors.</summary>
[TestClass]
public sealed class FetchRetryObservationFactoryTests
{
    private static readonly DateTimeOffset Now = new(1994, 11, 6, 8, 49, 37, TimeSpan.Zero);

    [TestMethod]
    public void Retry_after_is_attached_for_429_and_503_only()
    {
        var a = FetchRetryObservationFactory.FromHttpResponse(1, 429, "120", Now)!;
        Assert.AreEqual(TimeSpan.FromSeconds(120), a.RetryAfter);
        Assert.AreEqual(FetchRetryFailure.HttpStatus, a.Failure);
        Assert.AreEqual(429, a.HttpStatus);
        Assert.AreEqual(TimeSpan.FromSeconds(5), FetchRetryObservationFactory.FromHttpResponse(2, 503, "5", Now)!.RetryAfter);
        foreach (var status in new[] { 408, 500, 502, 504, 404 })
            Assert.IsNull(FetchRetryObservationFactory.FromHttpResponse(1, status, "120", Now)!.RetryAfter, status.ToString());
    }

    [TestMethod]
    public void Absent_or_malformed_header_leaves_retry_after_null()
    {
        foreach (var raw in new string?[] { null, "", "  ", "-5", "1.5", "soon", "Sunday, 06-Nov-94 08:49:37 GMT" })
            Assert.IsNull(FetchRetryObservationFactory.FromHttpResponse(1, 429, raw, Now)!.RetryAfter, raw);
    }

    [TestMethod]
    public void Http_date_header_is_relative_to_trusted_now()
    {
        var o = FetchRetryObservationFactory.FromHttpResponse(1, 503, "Sun, 06 Nov 1994 08:51:37 GMT", Now)!;
        Assert.AreEqual(TimeSpan.FromMinutes(2), o.RetryAfter);
    }

    [TestMethod]
    public void Successes_invalid_statuses_and_attempts_fail_closed()
    {
        foreach (var status in new[] { 200, 204, 299, 99, 600, 0, -1, int.MaxValue })
            Assert.IsNull(FetchRetryObservationFactory.FromHttpResponse(1, status, "5", Now), status.ToString());
        foreach (var attempt in new[] { 0, -1, int.MinValue })
            Assert.IsNull(FetchRetryObservationFactory.FromHttpResponse(attempt, 429, "5", Now), attempt.ToString());
    }

    [TestMethod]
    public void Composes_with_the_retry_policy()
    {
        var schedule = SourceFetchRetrySchedule.Default;
        var honoured = FetchRetryPolicy.Decide(schedule, FetchRetryObservationFactory.FromHttpResponse(1, 429, "120", Now));
        Assert.IsTrue(honoured.Retry);
        Assert.IsTrue(honoured.Delay >= TimeSpan.FromSeconds(120));

        var tooLong = FetchRetryPolicy.Decide(schedule, FetchRetryObservationFactory.FromHttpResponse(1, 429, "86400", Now));
        Assert.IsFalse(tooLong.Retry);
        Assert.AreEqual("retry_after_exceeds_limit", tooLong.Reason);

        var ignored = FetchRetryPolicy.Decide(schedule, FetchRetryObservationFactory.FromHttpResponse(1, 500, "86400", Now));
        Assert.IsTrue(ignored.Retry);

        var success = FetchRetryPolicy.Decide(schedule, FetchRetryObservationFactory.FromHttpResponse(1, 200, null, Now));
        Assert.IsFalse(success.Retry);
        Assert.AreEqual("invalid_observation", success.Reason);
    }
}
