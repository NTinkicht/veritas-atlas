using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 3 (#131): a re-fetch with identical bytes writes no new raw row, changed
/// bytes write a new immutable row, and malformed digests are refused rather than normalised.
/// </summary>
[TestClass]
public sealed class RawRetrievalDedupTests
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);

    private static void AssertRefused(RawRetrievalDedupDecision decision, string reason)
    {
        Assert.AreEqual(RawRetrievalDedupAction.Refused, decision.Action);
        Assert.AreEqual(reason, decision.Reason);
    }

    [TestMethod]
    public void First_retrieval_is_stored()
    {
        var decision = RawRetrievalDedup.Decide(null, A);
        Assert.AreEqual(RawRetrievalDedupAction.StoreNew, decision.Action);
        Assert.IsNull(decision.Reason);
    }

    [TestMethod]
    public void Identical_digest_is_a_duplicate_and_writes_no_new_row()
    {
        Assert.AreEqual(RawRetrievalDedupAction.Duplicate, RawRetrievalDedup.Decide(A, A).Action);
    }

    [TestMethod]
    public void Different_digest_is_a_changed_content_new_immutable_row()
    {
        Assert.AreEqual(RawRetrievalDedupAction.ContentChanged, RawRetrievalDedup.Decide(A, B).Action);
    }

    [TestMethod]
    public void Malformed_new_digest_is_refused_even_when_no_prior_exists()
    {
        foreach (var bad in new string?[]
        {
            null, "", " ", new string('a', 63), new string('a', 65), new string('A', 64),
            new string('g', 64), A + "\n", " " + A, "sha256:" + A,
        })
            AssertRefused(RawRetrievalDedup.Decide(null, bad), "invalid_new_digest");
    }

    [TestMethod]
    public void Uppercase_or_padded_digest_is_never_normalised_into_a_duplicate()
    {
        AssertRefused(RawRetrievalDedup.Decide(A, A.ToUpperInvariant()), "invalid_new_digest");
        AssertRefused(RawRetrievalDedup.Decide(A, A + "\n"), "invalid_new_digest");
    }

    [TestMethod]
    public void Malformed_prior_digest_is_refused_not_treated_as_changed_or_duplicate()
    {
        foreach (var bad in new[] { "", " ", new string('a', 63), new string('A', 64), A + "\n" })
            AssertRefused(RawRetrievalDedup.Decide(bad, A), "invalid_prior_digest");
    }

    [TestMethod]
    public void Invalid_new_digest_wins_over_invalid_prior_digest()
    {
        AssertRefused(RawRetrievalDedup.Decide("bad", "also-bad"), "invalid_new_digest");
    }
}
