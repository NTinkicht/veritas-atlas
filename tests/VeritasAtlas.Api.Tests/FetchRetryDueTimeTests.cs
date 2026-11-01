using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class FetchRetryDueTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Retry_decision_yields_now_plus_delay() =>
        Assert.AreEqual(Now.AddSeconds(4), FetchRetryDueTime.From(Now, FetchRetryDecision.After(TimeSpan.FromSeconds(4))));

    [TestMethod]
    public void Zero_delay_is_allowed() =>
        Assert.AreEqual(Now, FetchRetryDueTime.From(Now, FetchRetryDecision.After(TimeSpan.Zero)));

    [TestMethod]
    public void Null_decision_fails_closed() => Assert.IsNull(FetchRetryDueTime.From(Now, null));

    [TestMethod]
    public void Stop_decision_fails_closed() =>
        Assert.IsNull(FetchRetryDueTime.From(Now, FetchRetryDecision.Stop("attempts_exhausted")));

    [TestMethod]
    public void Retry_with_reason_fails_closed() =>
        Assert.IsNull(FetchRetryDueTime.From(Now, new FetchRetryDecision(true, TimeSpan.FromSeconds(1), "x")));

    [TestMethod]
    public void Negative_delay_fails_closed() =>
        Assert.IsNull(FetchRetryDueTime.From(Now, FetchRetryDecision.After(TimeSpan.FromSeconds(-1))));

    [TestMethod]
    public void Above_bound_delay_fails_closed() =>
        Assert.IsNull(FetchRetryDueTime.From(Now, FetchRetryDecision.After(TimeSpan.FromHours(1) + TimeSpan.FromTicks(1))));

    [TestMethod]
    public void Upper_bound_delay_is_allowed() =>
        Assert.AreEqual(Now.AddHours(1), FetchRetryDueTime.From(Now, FetchRetryDecision.After(TimeSpan.FromHours(1))));

    [TestMethod]
    public void Overflow_fails_closed() =>
        Assert.IsNull(FetchRetryDueTime.From(DateTimeOffset.MaxValue.AddSeconds(-1), FetchRetryDecision.After(TimeSpan.FromSeconds(2))));

    [TestMethod]
    public void Positive_offset_local_overflow_fails_closed() =>
        Assert.IsNull(FetchRetryDueTime.From(
            new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.FromHours(1)),
            FetchRetryDecision.After(TimeSpan.FromSeconds(2))));

    [TestMethod]
    public void Composes_with_real_policy_output()
    {
        var obs = FetchRetryObservationFactory.FromTransportFailure(2, FetchRetryFailure.Timeout);
        var decision = FetchRetryPolicy.Decide(SourceFetchRetrySchedule.Default, obs);
        Assert.AreEqual(Now.AddSeconds(4), FetchRetryDueTime.From(Now, decision));
    }
}
