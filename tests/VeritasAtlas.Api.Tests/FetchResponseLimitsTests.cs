using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-04 slice 3 (#115): response size and time limit vectors for independent
/// source fetches — exact boundaries, partial bodies, Content-Length mismatch,
/// invalid limits/observations and fixed precedence, all fail closed.
/// </summary>
[TestClass]
public sealed class FetchResponseLimitsTests
{
    private const long Max = 1_000_000;
    private static readonly TimeSpan MaxTime = TimeSpan.FromSeconds(30);
    private static readonly SourceFetchLimits Limits = new(Max, MaxTime);

    private static FetchResponseObservation Done(long? declared, long read, TimeSpan? elapsed = null, bool complete = true)
        => new(declared, read, elapsed ?? TimeSpan.FromSeconds(1), complete);

    private static void AssertDenied(FetchLimitDecision decision, string reason)
    {
        Assert.IsFalse(decision.Admitted);
        Assert.AreEqual(reason, decision.Reason);
    }

    private static void AssertAdmitted(FetchLimitDecision decision)
    {
        Assert.IsTrue(decision.Admitted);
        Assert.IsNull(decision.Reason);
    }

    [TestMethod]
    public void Completed_response_within_limits_is_admitted_including_exact_boundaries()
    {
        AssertAdmitted(FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(1234, 1234)));
        AssertAdmitted(FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(null, 1234)));
        AssertAdmitted(FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(Max, Max, MaxTime)));
        AssertAdmitted(FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void One_byte_or_one_tick_over_a_limit_is_denied()
    {
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(null, Max + 1)),
            "response_too_large");
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(Max + 1, Max + 1)),
            "content_length_exceeds_limit");
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(10, 10, MaxTime + TimeSpan.FromTicks(1))),
            "fetch_timeout");
    }

    [TestMethod]
    public void Partial_body_is_never_admitted_even_within_limits()
    {
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(1000, 400, complete: false)),
            "incomplete_response");
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(null, 400, complete: false)),
            "incomplete_response");
    }

    [TestMethod]
    public void Body_length_different_from_declared_content_length_is_denied()
    {
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(1000, 999)),
            "content_length_mismatch");
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(1000, 1001)),
            "content_length_mismatch");
    }

    [TestMethod]
    public void Declared_length_over_the_limit_aborts_before_any_byte_is_read()
    {
        AssertDenied(
            FetchResponseLimitGuard.CheckProgress(Limits, Max + 1, 0, TimeSpan.Zero),
            "content_length_exceeds_limit");
        AssertDenied(
            FetchResponseLimitGuard.CheckProgress(Limits, long.MaxValue, 0, TimeSpan.Zero),
            "content_length_exceeds_limit");
    }

    [TestMethod]
    public void Streaming_progress_aborts_on_size_or_time_and_continues_otherwise()
    {
        AssertAdmitted(FetchResponseLimitGuard.CheckProgress(Limits, null, Max, MaxTime));
        AssertAdmitted(FetchResponseLimitGuard.CheckProgress(Limits, Max, 10, TimeSpan.FromSeconds(2)));
        AssertDenied(FetchResponseLimitGuard.CheckProgress(Limits, null, Max + 1, TimeSpan.Zero), "response_too_large");
        AssertDenied(
            FetchResponseLimitGuard.CheckProgress(Limits, null, 0, MaxTime + TimeSpan.FromTicks(1)),
            "fetch_timeout");
    }

    [TestMethod]
    public void Negative_observations_fail_closed()
    {
        AssertDenied(FetchResponseLimitGuard.CheckProgress(Limits, -1, 0, TimeSpan.Zero), "invalid_observation");
        AssertDenied(FetchResponseLimitGuard.CheckProgress(Limits, null, -1, TimeSpan.Zero), "invalid_observation");
        AssertDenied(
            FetchResponseLimitGuard.CheckProgress(Limits, null, 0, TimeSpan.FromTicks(-1)),
            "invalid_observation");
        AssertDenied(FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(null, -5)), "invalid_observation");
        AssertDenied(FetchResponseLimitGuard.EvaluateCompleted(Limits, null), "invalid_observation");
    }

    [TestMethod]
    public void Limits_outside_the_admitted_bounds_fail_closed()
    {
        var invalid = new SourceFetchLimits?[]
        {
            null,
            new(0, MaxTime),
            new(-1, MaxTime),
            new(SourceFetchLimits.MaxAdmittedResponseBytes + 1, MaxTime),
            new(long.MaxValue, MaxTime),
            new(Max, TimeSpan.Zero),
            new(Max, SourceFetchLimits.MinDuration - TimeSpan.FromTicks(1)),
            new(Max, SourceFetchLimits.MaxAdmittedDuration + TimeSpan.FromTicks(1)),
            new(Max, TimeSpan.MaxValue),
            new(Max, TimeSpan.FromSeconds(-1)),
        };
        foreach (var limits in invalid)
        {
            AssertDenied(FetchResponseLimitGuard.CheckProgress(limits, null, 0, TimeSpan.Zero), "invalid_limits");
            AssertDenied(FetchResponseLimitGuard.EvaluateCompleted(limits, Done(0, 0)), "invalid_limits");
            AssertDenied(FetchResponseLimitGuard.EvaluateCompleted(limits, null), "invalid_limits");
        }
    }

    [TestMethod]
    public void Admitted_bounds_themselves_are_valid_limits()
    {
        var smallest = new SourceFetchLimits(SourceFetchLimits.MinResponseBytes, SourceFetchLimits.MinDuration);
        var largest = new SourceFetchLimits(
            SourceFetchLimits.MaxAdmittedResponseBytes,
            SourceFetchLimits.MaxAdmittedDuration);
        AssertAdmitted(FetchResponseLimitGuard.EvaluateCompleted(smallest, Done(1, 1, TimeSpan.FromSeconds(1))));
        AssertDenied(FetchResponseLimitGuard.EvaluateCompleted(smallest, Done(null, 2)), "response_too_large");
        AssertAdmitted(FetchResponseLimitGuard.EvaluateCompleted(
            largest,
            Done(SourceFetchLimits.MaxAdmittedResponseBytes, SourceFetchLimits.MaxAdmittedResponseBytes, TimeSpan.FromSeconds(120))));
        Assert.AreEqual(64L * 1024 * 1024, SourceFetchLimits.MaxAdmittedResponseBytes);
        Assert.AreEqual(TimeSpan.FromSeconds(120), SourceFetchLimits.MaxAdmittedDuration);
    }

    [TestMethod]
    public void Precedence_is_fixed_when_several_rules_fail()
    {
        var late = MaxTime + TimeSpan.FromSeconds(1);
        // Everything wrong: invalid limits win.
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(new SourceFetchLimits(0, MaxTime), Done(-1, -1, late, false)),
            "invalid_limits");
        // Negative value beats size/time.
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(Max + 1, -1, late, false)),
            "invalid_observation");
        // Declared length beats bytes read, time, partial and mismatch.
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(Max + 1, Max + 2, late, false)),
            "content_length_exceeds_limit");
        // Bytes read beats time and partial.
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(null, Max + 1, late, false)),
            "response_too_large");
        // Time beats partial and mismatch.
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(100, 50, late, false)),
            "fetch_timeout");
        // Partial beats mismatch.
        AssertDenied(
            FetchResponseLimitGuard.EvaluateCompleted(Limits, Done(100, 50, complete: false)),
            "incomplete_response");
    }

    [TestMethod]
    public void Denials_map_to_the_fetch_attempt_ledger_failure_classes()
    {
        Assert.IsNull(FetchResponseLimitGuard.LedgerFailureClass(FetchLimitDecision.Admit));
        Assert.AreEqual("size_limit", FetchResponseLimitGuard.LedgerFailureClass(FetchLimitDecision.Deny("content_length_exceeds_limit")));
        Assert.AreEqual("size_limit", FetchResponseLimitGuard.LedgerFailureClass(FetchLimitDecision.Deny("response_too_large")));
        Assert.AreEqual("timeout", FetchResponseLimitGuard.LedgerFailureClass(FetchLimitDecision.Deny("fetch_timeout")));
        foreach (var reason in new[] { "invalid_limits", "invalid_observation", "incomplete_response", "content_length_mismatch", "unknown" })
        {
            Assert.AreEqual("policy_denied", FetchResponseLimitGuard.LedgerFailureClass(FetchLimitDecision.Deny(reason)));
        }
        Assert.AreEqual("policy_denied", FetchResponseLimitGuard.LedgerFailureClass(null));
        // An inconsistent decision (admitted yet carrying a reason) fails closed.
        Assert.AreEqual("policy_denied", FetchResponseLimitGuard.LedgerFailureClass(new FetchLimitDecision(true, "forged")));
    }
}
