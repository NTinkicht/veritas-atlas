using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class RetentionDeletionGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    private static RetentionRequest R(RetentionRecordKind kind = RetentionRecordKind.WorkspaceItem, string? rec = "t1", string? req = "t1",
        string? op = "op-1", DateTimeOffset? until = null, bool hold = false, bool live = false, bool audit = true) =>
        new(kind, rec, req, op, until ?? Now.AddDays(-1), Now, hold, live, audit);

    private static void Is(RetentionRequest r, RetentionOutcome o, string? code = null)
    {
        var d = RetentionDeletionGate.Evaluate(r);
        Assert.AreEqual(o, d.Outcome);
        Assert.AreEqual(code, d.DenialCode);
    }

    [TestMethod]
    [DataRow(RetentionRecordKind.WorkspaceItem)]
    [DataRow(RetentionRecordKind.RawRetrieval)]
    public void ElapsedUnreferencedAuditedRecord_IsAllowed(RetentionRecordKind k) => Is(R(k), RetentionOutcome.Allow);

    [TestMethod]
    public void RetentionBoundary_IsInclusive() => Is(R(until: Now), RetentionOutcome.Allow);

    [TestMethod]
    public void BeforeRetentionEnd_IsDenied() => Is(R(until: Now.AddTicks(1)), RetentionOutcome.Deny, RetentionDenials.RetentionNotElapsed);

    [TestMethod]
    public void MissingRetentionEnd_IsDenied() =>
        Is(new RetentionRequest(RetentionRecordKind.WorkspaceItem, "t1", "t1", "op", null, Now, false, false, true), RetentionOutcome.Deny, RetentionDenials.RetentionNotElapsed);

    [TestMethod]
    [DataRow(RetentionRecordKind.AuditLedger)]
    [DataRow(RetentionRecordKind.Attestation)]
    [DataRow(RetentionRecordKind.VerifiedEvidence)]
    [DataRow(RetentionRecordKind.PublicationRevision)]
    public void ImmutableKinds_AreNeverDeletable(RetentionRecordKind k) => Is(R(k), RetentionOutcome.Deny, RetentionDenials.Immutable);

    [TestMethod]
    [DataRow(RetentionRecordKind.Unknown)]
    [DataRow((RetentionRecordKind)99)]
    public void UnknownKind_IsDenied(RetentionRecordKind k) => Is(R(k), RetentionOutcome.Deny, RetentionDenials.UnknownKind);

    [TestMethod]
    public void LegalHold_Denies() => Is(R(hold: true), RetentionOutcome.Deny, RetentionDenials.LegalHold);

    [TestMethod]
    public void LiveReference_Denies() => Is(R(RetentionRecordKind.RawRetrieval, live: true), RetentionOutcome.Deny, RetentionDenials.LiveReference);

    [TestMethod]
    public void MissingAuditTrail_Denies() => Is(R(audit: false), RetentionOutcome.Deny, RetentionDenials.AuditMissing);

    [TestMethod]
    [DataRow("t1", "t2")]
    [DataRow("t1", null)]
    [DataRow(null, "t1")]
    [DataRow("", "")]
    [DataRow("t1", "T1")]
    public void TenantMismatchOrBlank_IsNotFound_EvenForImmutableKinds(string? rec, string? req) =>
        Is(R(RetentionRecordKind.AuditLedger, rec, req), RetentionOutcome.Deny, RetentionDenials.NotFound);

    [TestMethod]
    [DataRow(null)]
    [DataRow(" ")]
    public void MissingOperationId_IsDenied(string? op) => Is(R(op: op), RetentionOutcome.Deny, RetentionDenials.OperationIdMissing);

    [TestMethod]
    public void ImmutableOutranksHoldAndElapsedRetention() =>
        Is(R(RetentionRecordKind.AuditLedger, hold: true, live: true), RetentionOutcome.Deny, RetentionDenials.Immutable);

    [TestMethod]
    public void NullRequest_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => RetentionDeletionGate.Evaluate(null!));
}
