using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-06 slice 13 (#151): transport failure to retry observation vectors.</summary>
[TestClass]
public sealed class FetchRetryTransportFailureTests
{
    [TestMethod]
    public void Retryable_failures_have_no_status_or_retry_after_and_back_off_until_exhausted()
    {
        var schedule = SourceFetchRetrySchedule.Default;
        foreach (var failure in new[] { FetchRetryFailure.Network, FetchRetryFailure.Timeout, FetchRetryFailure.IncompleteResponse })
        {
            var o = FetchRetryObservationFactory.FromTransportFailure(1, failure)!;
            Assert.AreEqual(failure, o.Failure);
            Assert.IsNull(o.HttpStatus);
            Assert.IsNull(o.RetryAfter);
            Assert.IsTrue(FetchRetryPolicy.Decide(schedule, o).Retry, failure.ToString());
            var last = FetchRetryObservationFactory.FromTransportFailure(schedule.MaxAttempts, failure);
            Assert.AreEqual("attempts_exhausted", FetchRetryPolicy.Decide(schedule, last).Reason, failure.ToString());
        }
    }

    [TestMethod]
    public void Size_limit_and_policy_denial_are_never_retried()
    {
        var schedule = SourceFetchRetrySchedule.Default;
        Assert.AreEqual("size_limit_not_retryable",
            FetchRetryPolicy.Decide(schedule, FetchRetryObservationFactory.FromTransportFailure(1, FetchRetryFailure.SizeLimit)).Reason);
        Assert.AreEqual("policy_denied_not_retryable",
            FetchRetryPolicy.Decide(schedule, FetchRetryObservationFactory.FromTransportFailure(1, FetchRetryFailure.PolicyDenied)).Reason);
    }

    [TestMethod]
    public void Http_status_invalid_attempt_and_undefined_failure_fail_closed()
    {
        Assert.IsNull(FetchRetryObservationFactory.FromTransportFailure(1, FetchRetryFailure.HttpStatus));
        Assert.IsNull(FetchRetryObservationFactory.FromTransportFailure(0, FetchRetryFailure.Timeout));
        Assert.IsNull(FetchRetryObservationFactory.FromTransportFailure(-1, FetchRetryFailure.Network));
        Assert.IsNull(FetchRetryObservationFactory.FromTransportFailure(1, (FetchRetryFailure)99));
    }
}
