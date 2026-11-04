using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class PublicationVersionGuardTests
{
    private static GuardRequest R(long expected = 3, long current = 3, string? op = "op-1", LifecycleAction action = LifecycleAction.Approve,
        string? priorOp = null, LifecycleAction? priorAction = null, string? approved = "d1", string? digest = "d1") =>
        new(expected, current, op, action, priorOp, priorAction, approved, digest);

    private static void Is(GuardRequest r, GuardOutcome outcome, string? code = null)
    {
        var x = PublicationVersionGuard.Evaluate(r);
        Assert.AreEqual(outcome, x.Outcome);
        Assert.AreEqual(code, x.DenialCode);
    }

    [TestMethod]
    public void MatchingVersion_Applies() => Is(R(), GuardOutcome.Apply);

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void MissingOperationId_IsDenied(string? op) => Is(R(op: op), GuardOutcome.Deny, GuardDenials.OperationIdMissing);

    [TestMethod]
    [DataRow(2L, 3L)]
    [DataRow(4L, 3L)]
    [DataRow(-1L, -1L)]
    [DataRow(0L, -1L)]
    public void StaleOrInvalidVersion_IsDenied(long expected, long current) =>
        Is(R(expected, current), GuardOutcome.Deny, GuardDenials.StaleVersion);

    [TestMethod]
    public void ExactRetryOfCommittedOperation_IsReplay() =>
        Is(R(expected: 3, current: 4, priorOp: "op-1", priorAction: LifecycleAction.Approve), GuardOutcome.Replay);

    [TestMethod]
    public void SameOperationIdForDifferentAction_IsConflict() =>
        Is(R(expected: 3, current: 4, action: LifecycleAction.Publish, priorOp: "op-1", priorAction: LifecycleAction.Approve),
            GuardOutcome.Deny, GuardDenials.OperationConflict);

    [TestMethod]
    public void SameOperationIdAfterFurtherChanges_IsConflictNotReplay() =>
        Is(R(expected: 3, current: 6, priorOp: "op-1", priorAction: LifecycleAction.Approve), GuardOutcome.Deny, GuardDenials.OperationConflict);

    [TestMethod]
    public void SameOperationIdWithoutAdvancedVersion_IsConflict() =>
        Is(R(expected: 3, current: 3, priorOp: "op-1", priorAction: LifecycleAction.Approve), GuardOutcome.Deny, GuardDenials.OperationConflict);

    [TestMethod]
    public void OperationIdCaseDiffers_IsNotAReplay() =>
        Is(R(expected: 3, current: 3, op: "OP-1", priorOp: "op-1", priorAction: LifecycleAction.Approve), GuardOutcome.Apply);

    [TestMethod]
    public void DifferentOperationOnStaleVersion_IsStale() =>
        Is(R(expected: 3, current: 4, op: "op-2", priorOp: "op-1", priorAction: LifecycleAction.Approve), GuardOutcome.Deny, GuardDenials.StaleVersion);

    [TestMethod]
    public void ReplayDoesNotOverflowAtMaxVersion() =>
        Is(R(expected: long.MaxValue, current: long.MaxValue, priorOp: "op-1", priorAction: LifecycleAction.Approve),
            GuardOutcome.Deny, GuardDenials.OperationConflict);

    [TestMethod]
    public void Publish_WithMatchingApprovedDigest_Applies() => Is(R(action: LifecycleAction.Publish), GuardOutcome.Apply);

    [TestMethod]
    [DataRow("d1", "d2")]
    [DataRow(null, "d1")]
    [DataRow("d1", null)]
    [DataRow("", "")]
    [DataRow("D1", "d1")]
    public void Publish_EditedOrUnboundAfterApproval_IsDenied(string? approved, string? current) =>
        Is(R(action: LifecycleAction.Publish, approved: approved, digest: current), GuardOutcome.Deny, GuardDenials.ApprovalDigestMismatch);

    [TestMethod]
    public void NonPublishActions_DoNotRequireDigest() =>
        Is(R(action: LifecycleAction.Withdraw, approved: null, digest: null), GuardOutcome.Apply);

    [TestMethod]
    public void NullRequest_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => PublicationVersionGuard.Evaluate(null!));
}
