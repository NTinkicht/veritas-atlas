using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 1 (#127): deterministic, fail-closed retry/backoff vectors for source
/// fetches — exact backoff sequence and cap, Retry-After only on 429/503 and only up to the
/// ceiling, non-retryable classes and statuses, exhaustion at exactly MaxAttempts, invalid
/// schedules/observations and fixed reason precedence.
/// </summary>
[TestClass]
public sealed class FetchRetryPolicyTests
{
    private static readonly SourceFetchRetrySchedule Five = new(
        5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

    private static FetchRetryObservation Failed(
        int attempt, FetchRetryFailure failure, int? status = null, TimeSpan? retryAfter = null)
        => new(attempt, failure, status, retryAfter);

    private static FetchRetryObservation Http(int attempt, int status, TimeSpan? retryAfter = null)
        => Failed(attempt, FetchRetryFailure.HttpStatus, status, retryAfter);

    private static void AssertRetry(FetchRetryDecision decision, TimeSpan delay)
    {
        Assert.IsTrue(decision.Retry, decision.Reason);
        Assert.AreEqual(delay, decision.Delay);
        Assert.IsNull(decision.Reason);
    }

    private static void AssertStop(FetchRetryDecision decision, string reason)
    {
        Assert.IsFalse(decision.Retry);
        Assert.AreEqual(TimeSpan.Zero, decision.Delay);
        Assert.AreEqual(reason, decision.Reason);
    }

    [TestMethod]
    public void Backoff_doubles_from_the_base_and_is_capped()
    {
        var delays = Enumerable.Range(1, 4)
            .Select(attempt => FetchRetryPolicy.Decide(Five, Failed(attempt, FetchRetryFailure.Network)))
            .Select(decision => decision.Delay.TotalSeconds)
            .ToArray();
        CollectionAssert.AreEqual(new double[] { 2, 4, 8, 10 }, delays);
    }

    [TestMethod]
    public void Default_schedule_retries_twice_then_stops()
    {
        var schedule = SourceFetchRetrySchedule.Default;
        AssertRetry(FetchRetryPolicy.Decide(schedule, Failed(1, FetchRetryFailure.Timeout)), TimeSpan.FromSeconds(2));
        AssertRetry(FetchRetryPolicy.Decide(schedule, Failed(2, FetchRetryFailure.Timeout)), TimeSpan.FromSeconds(4));
        AssertStop(FetchRetryPolicy.Decide(schedule, Failed(3, FetchRetryFailure.Timeout)), "attempts_exhausted");
    }

    [TestMethod]
    [DataRow(FetchRetryFailure.Network)]
    [DataRow(FetchRetryFailure.Timeout)]
    [DataRow(FetchRetryFailure.IncompleteResponse)]
    public void Transient_failures_are_retried(FetchRetryFailure failure) =>
        AssertRetry(FetchRetryPolicy.Decide(Five, Failed(1, failure)), TimeSpan.FromSeconds(2));

    [TestMethod]
    [DataRow(408)]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(502)]
    [DataRow(503)]
    [DataRow(504)]
    public void Transient_http_statuses_are_retried(int status) =>
        AssertRetry(FetchRetryPolicy.Decide(Five, Http(2, status)), TimeSpan.FromSeconds(4));

    [TestMethod]
    [DataRow(100)]
    [DataRow(301)]
    [DataRow(304)]
    [DataRow(400)]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    [DataRow(410)]
    [DataRow(451)]
    [DataRow(501)]
    [DataRow(505)]
    [DataRow(599)]
    public void Other_http_statuses_are_never_retried(int status) =>
        AssertStop(FetchRetryPolicy.Decide(Five, Http(1, status)), "http_status_not_retryable");

    [TestMethod]
    public void Policy_denials_and_size_limit_breaches_are_never_retried()
    {
        AssertStop(
            FetchRetryPolicy.Decide(Five, Failed(1, FetchRetryFailure.PolicyDenied)),
            "policy_denied_not_retryable");
        AssertStop(
            FetchRetryPolicy.Decide(Five, Failed(1, FetchRetryFailure.SizeLimit)),
            "size_limit_not_retryable");
    }

    [TestMethod]
    public void Exhaustion_is_exactly_at_max_attempts()
    {
        AssertRetry(FetchRetryPolicy.Decide(Five, Failed(4, FetchRetryFailure.Network)), TimeSpan.FromSeconds(10));
        AssertStop(FetchRetryPolicy.Decide(Five, Failed(5, FetchRetryFailure.Network)), "attempts_exhausted");
        var single = Five with { MaxAttempts = 1 };
        AssertStop(FetchRetryPolicy.Decide(single, Failed(1, FetchRetryFailure.Network)), "attempts_exhausted");
    }

    [TestMethod]
    public void Retry_after_on_429_and_503_raises_the_delay_up_to_the_ceiling()
    {
        AssertRetry(FetchRetryPolicy.Decide(Five, Http(1, 429, TimeSpan.FromSeconds(25))), TimeSpan.FromSeconds(25));
        AssertRetry(FetchRetryPolicy.Decide(Five, Http(1, 503, TimeSpan.FromSeconds(30))), TimeSpan.FromSeconds(30));
        // A Retry-After shorter than the backoff never shortens it.
        AssertRetry(FetchRetryPolicy.Decide(Five, Http(3, 429, TimeSpan.FromSeconds(1))), TimeSpan.FromSeconds(8));
        AssertRetry(FetchRetryPolicy.Decide(Five, Http(1, 429, TimeSpan.Zero)), TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void Retry_after_above_the_ceiling_stops_instead_of_waiting()
    {
        AssertStop(
            FetchRetryPolicy.Decide(Five, Http(1, 429, TimeSpan.FromSeconds(30) + TimeSpan.FromTicks(1))),
            "retry_after_exceeds_limit");
        AssertStop(
            FetchRetryPolicy.Decide(Five, Http(1, 503, TimeSpan.FromHours(2))),
            "retry_after_exceeds_limit");
    }

    [TestMethod]
    public void Retry_after_is_ignored_for_other_retryable_failures()
    {
        AssertRetry(FetchRetryPolicy.Decide(Five, Http(1, 500, TimeSpan.FromHours(2))), TimeSpan.FromSeconds(2));
        AssertRetry(
            FetchRetryPolicy.Decide(Five, Failed(1, FetchRetryFailure.Timeout, retryAfter: TimeSpan.FromHours(2))),
            TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void Invalid_schedules_are_refused()
    {
        AssertStop(FetchRetryPolicy.Decide(null, Failed(1, FetchRetryFailure.Network)), "invalid_schedule");
        foreach (var schedule in new[]
        {
            Five with { MaxAttempts = 0 },
            Five with { MaxAttempts = 6 },
            Five with { BaseDelay = TimeSpan.FromMilliseconds(999) },
            Five with { BaseDelay = TimeSpan.FromSeconds(61), MaxDelay = TimeSpan.FromMinutes(2) },
            Five with { MaxDelay = TimeSpan.FromSeconds(1) },
            Five with { MaxDelay = TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1) },
            Five with { MaxRetryAfter = TimeSpan.FromTicks(-1) },
            Five with { MaxRetryAfter = TimeSpan.FromHours(1) + TimeSpan.FromTicks(1) },
        })
        {
            AssertStop(FetchRetryPolicy.Decide(schedule, Failed(1, FetchRetryFailure.Network)), "invalid_schedule");
        }
    }

    [TestMethod]
    public void Admitted_schedule_bounds_are_inclusive()
    {
        var widest = new SourceFetchRetrySchedule(
            5, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1));
        AssertRetry(FetchRetryPolicy.Decide(widest, Http(4, 429, TimeSpan.FromHours(1))), TimeSpan.FromHours(1));
        var narrowest = new SourceFetchRetrySchedule(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.Zero);
        AssertRetry(FetchRetryPolicy.Decide(narrowest, Failed(1, FetchRetryFailure.Network)), TimeSpan.FromSeconds(1));
        AssertStop(FetchRetryPolicy.Decide(narrowest, Http(1, 429, TimeSpan.FromTicks(1))), "retry_after_exceeds_limit");
    }

    [TestMethod]
    public void Invalid_observations_are_refused()
    {
        foreach (var observation in new FetchRetryObservation?[]
        {
            null,
            Failed(0, FetchRetryFailure.Network),
            Failed(6, FetchRetryFailure.Network),
            Failed(1, (FetchRetryFailure)99),
            Failed(1, FetchRetryFailure.Network, status: 503),
            Failed(1, FetchRetryFailure.PolicyDenied, status: 403),
            Failed(1, FetchRetryFailure.HttpStatus),
            Http(1, 200),
            Http(1, 204),
            Http(1, 99),
            Http(1, 600),
            Failed(1, FetchRetryFailure.Network, retryAfter: TimeSpan.FromTicks(-1)),
        })
        {
            AssertStop(FetchRetryPolicy.Decide(Five, observation), "invalid_observation");
        }
    }

    [TestMethod]
    public void Reason_precedence_is_fixed()
    {
        // An invalid schedule wins over an invalid observation.
        AssertStop(FetchRetryPolicy.Decide(Five with { MaxAttempts = 0 }, null), "invalid_schedule");
        // A non-retryable failure wins over exhaustion.
        AssertStop(FetchRetryPolicy.Decide(Five, Failed(5, FetchRetryFailure.PolicyDenied)), "policy_denied_not_retryable");
        AssertStop(FetchRetryPolicy.Decide(Five, Http(5, 404)), "http_status_not_retryable");
        // Exhaustion wins over an excessive Retry-After.
        AssertStop(FetchRetryPolicy.Decide(Five, Http(5, 429, TimeSpan.FromHours(2))), "attempts_exhausted");
    }
}
