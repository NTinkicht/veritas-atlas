using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 7 (#139): a retrieval checkpoint advances only one step at a time, only after
/// the raw record is persisted, and never backwards or with a rewritten key.
/// </summary>
[TestClass]
public sealed class RetrievalCheckpointAdvanceTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    private static RetrievalCheckpoint Current(long seq = 3, string key = "k3", DateTime? at = null) =>
        new(seq, key, at ?? T0);

    private static RetrievalCheckpointProposal Proposal(
        long seq = 4, string key = "k4", DateTime? at = null, bool persisted = true) =>
        new(seq, key, at ?? T0.AddSeconds(5), persisted);

    private static void AssertRefused(RetrievalCheckpointDecision d, string reason)
    {
        Assert.AreEqual(RetrievalCheckpointAction.Refused, d.Action);
        Assert.AreEqual(reason, d.Reason);
    }

    [TestMethod]
    public void First_checkpoint_must_be_sequence_one()
    {
        Assert.AreEqual(
            RetrievalCheckpointAction.Advance,
            RetrievalCheckpointAdvance.Decide(null, Proposal(1, "k1")).Action);
        AssertRefused(RetrievalCheckpointAdvance.Decide(null, Proposal(2, "k2")), "sequence_gap");
    }

    [TestMethod]
    public void Next_sequence_advances_after_persistence()
    {
        var d = RetrievalCheckpointAdvance.Decide(Current(), Proposal());
        Assert.AreEqual(RetrievalCheckpointAction.Advance, d.Action);
        Assert.IsNull(d.Reason);
    }

    [TestMethod]
    public void Same_time_as_current_is_allowed_but_earlier_is_a_time_regression()
    {
        Assert.AreEqual(
            RetrievalCheckpointAction.Advance,
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(at: T0)).Action);
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(at: T0.AddTicks(-1))), "time_regression");
    }

    [TestMethod]
    public void Never_advances_before_the_raw_record_is_persisted()
    {
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(persisted: false)), "raw_not_persisted");
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(null, Proposal(1, "k1", persisted: false)), "raw_not_persisted");
    }

    [TestMethod]
    public void Identical_proposal_is_a_replay_and_a_rewritten_key_or_time_conflicts()
    {
        Assert.AreEqual(
            RetrievalCheckpointAction.Replay,
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(3, "k3", T0)).Action);
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(3, "other", T0)), "sequence_key_conflict");
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(3, "k3", T0.AddSeconds(1))),
            "sequence_key_conflict");
    }

    [TestMethod]
    public void Lower_sequence_is_a_regression_and_a_skip_is_a_gap()
    {
        AssertRefused(RetrievalCheckpointAdvance.Decide(Current(), Proposal(2, "k2")), "regression");
        AssertRefused(RetrievalCheckpointAdvance.Decide(Current(), Proposal(5, "k5")), "sequence_gap");
        AssertRefused(RetrievalCheckpointAdvance.Decide(Current(), Proposal(long.MaxValue, "kmax")), "sequence_gap");
    }

    [TestMethod]
    public void Checkpoint_at_the_maximum_sequence_cannot_overflow()
    {
        var max = Current(long.MaxValue, "kmax");
        Assert.AreEqual(
            RetrievalCheckpointAction.Replay,
            RetrievalCheckpointAdvance.Decide(max, Proposal(long.MaxValue, "kmax", T0)).Action);
        AssertRefused(RetrievalCheckpointAdvance.Decide(max, Proposal(4, "k4")), "regression");
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(long.MaxValue - 1, "k"), Proposal(1, "k1")), "regression");
    }

    [TestMethod]
    public void Invalid_proposals_are_refused_before_anything_else()
    {
        AssertRefused(RetrievalCheckpointAdvance.Decide(Current(), null), "invalid_proposed");
        foreach (var bad in new[]
        {
            Proposal(0, "k0"), Proposal(-1, "k"), Proposal(key: ""), Proposal(key: "  "),
            Proposal(key: new string('x', RetrievalCheckpointAdvance.MaxKeyLength + 1)),
            Proposal(at: DateTime.SpecifyKind(T0, DateTimeKind.Local)),
            Proposal(at: DateTime.SpecifyKind(T0, DateTimeKind.Unspecified)),
            Proposal(persisted: false) with { Sequence = 0 },
        })
            AssertRefused(RetrievalCheckpointAdvance.Decide(Current(), bad), "invalid_proposed");
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(key: null!)), "invalid_proposed");
    }

    [TestMethod]
    public void Maximum_length_key_is_accepted()
    {
        var key = new string('x', RetrievalCheckpointAdvance.MaxKeyLength);
        Assert.AreEqual(
            RetrievalCheckpointAction.Advance,
            RetrievalCheckpointAdvance.Decide(Current(), Proposal(key: key)).Action);
    }

    [TestMethod]
    public void Corrupt_stored_checkpoint_fails_closed()
    {
        foreach (var bad in new[]
        {
            Current(0), Current(-3), Current(key: ""), Current(key: " "),
            Current(key: new string('x', RetrievalCheckpointAdvance.MaxKeyLength + 1)),
            Current(at: DateTime.SpecifyKind(T0, DateTimeKind.Local)),
        })
            AssertRefused(RetrievalCheckpointAdvance.Decide(bad, Proposal()), "invalid_current");
    }

    [TestMethod]
    public void Invalid_proposal_wins_over_invalid_current_and_unpersisted()
    {
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(0), Proposal(0, persisted: false)), "invalid_proposed");
        AssertRefused(
            RetrievalCheckpointAdvance.Decide(Current(0), Proposal(persisted: false)), "invalid_current");
    }
}
