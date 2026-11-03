using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class ConfidenceLabelerTests
{
    private static ConfidenceInputs I(int sup = 2, int src = 2, int con = 0, int sps = 0, int stale = 0, int unk = 0, int unres = 0) =>
        new(sup, src, con, sps, stale, unk, unres);

    [TestMethod]
    public void CleanMultiSource_IsHigh_AndReproducible()
    {
        var a = ConfidenceLabeler.Label(I());
        var b = ConfidenceLabeler.Label(I());
        Assert.AreEqual(ConfidenceBand.High, a.Band);
        CollectionAssert.AreEqual(a.Reasons.ToList(), b.Reasons.ToList());
        CollectionAssert.AreEqual(new[] { ConfidenceReasons.MultipleIndependentSources }, a.Reasons.ToList());
    }

    [TestMethod]
    public void SingleSource_CapsAtMedium() =>
        Assert.AreEqual(ConfidenceBand.Medium, ConfidenceLabeler.Label(I(src: 1)).Band);

    [TestMethod]
    public void AnyMissingInput_IsInsufficient_NeverDefaulted()
    {
        var all = new[]
        {
            new ConfidenceInputs(null, 2, 0, 0, 0, 0, 0), new ConfidenceInputs(2, null, 0, 0, 0, 0, 0),
            new ConfidenceInputs(2, 2, null, 0, 0, 0, 0), new ConfidenceInputs(2, 2, 0, null, 0, 0, 0),
            new ConfidenceInputs(2, 2, 0, 0, null, 0, 0), new ConfidenceInputs(2, 2, 0, 0, 0, null, 0),
            new ConfidenceInputs(2, 2, 0, 0, 0, 0, null),
        };
        foreach (var x in all)
        {
            var l = ConfidenceLabeler.Label(x);
            Assert.AreEqual(ConfidenceBand.Insufficient, l.Band);
            CollectionAssert.AreEqual(new[] { ConfidenceReasons.InputMissing }, l.Reasons.ToList());
        }
    }

    [TestMethod]
    public void NoSupport_IsInsufficient()
    {
        Assert.AreEqual(ConfidenceBand.Insufficient, ConfidenceLabeler.Label(I(sup: 0)).Band);
        Assert.AreEqual(ConfidenceReasons.NoSupport, ConfidenceLabeler.Label(I(src: 0)).Reasons[0]);
    }

    [TestMethod]
    public void ConflictOrSuperseded_CapsAtLow()
    {
        Assert.AreEqual(ConfidenceBand.Low, ConfidenceLabeler.Label(I(con: 1)).Band);
        Assert.AreEqual(ConfidenceBand.Low, ConfidenceLabeler.Label(I(sps: 1)).Band);
    }

    [TestMethod]
    public void StaleUnknownUnresolved_EachDegradeOneBand_FlooredAtLow()
    {
        Assert.AreEqual(ConfidenceBand.Medium, ConfidenceLabeler.Label(I(stale: 1)).Band);
        Assert.AreEqual(ConfidenceBand.Medium, ConfidenceLabeler.Label(I(unk: 1)).Band);
        Assert.AreEqual(ConfidenceBand.Medium, ConfidenceLabeler.Label(I(unres: 1)).Band);
        Assert.AreEqual(ConfidenceBand.Low, ConfidenceLabeler.Label(I(stale: 1, unk: 1, unres: 1)).Band);
    }

    [TestMethod]
    public void Monotone_AddingNegativeSignalNeverRaisesBand()
    {
        foreach (var src in new[] { 1, 2 })
        foreach (var con in new[] { 0, 1 }) foreach (var sps in new[] { 0, 1 }) foreach (var st in new[] { 0, 1 })
        foreach (var unk in new[] { 0, 1 }) foreach (var unr in new[] { 0, 1 })
        {
            var baseBand = ConfidenceLabeler.Label(I(src: src, con: con, sps: sps, stale: st, unk: unk, unres: unr)).Band;
            Assert.IsTrue(ConfidenceLabeler.Label(I(src: src, con: con + 1, sps: sps, stale: st, unk: unk, unres: unr)).Band <= baseBand);
            Assert.IsTrue(ConfidenceLabeler.Label(I(src: src, con: con, sps: sps + 1, stale: st, unk: unk, unres: unr)).Band <= baseBand);
            Assert.IsTrue(ConfidenceLabeler.Label(I(src: src, con: con, sps: sps, stale: st + 1, unk: unk, unres: unr)).Band <= baseBand);
            Assert.IsTrue(ConfidenceLabeler.Label(I(src: src, con: con, sps: sps, stale: st, unk: unk + 1, unres: unr)).Band <= baseBand);
            Assert.IsTrue(ConfidenceLabeler.Label(I(src: src, con: con, sps: sps, stale: st, unk: unk, unres: unr + 1)).Band <= baseBand);
        }
    }

    [TestMethod]
    public void NegativeCounts_Rejected() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ConfidenceLabeler.Label(I(con: -1)));

    [TestMethod]
    public void BandIsCoarse_NoNumericScoreExposed() =>
        CollectionAssert.AreEquivalent(new[] { "Insufficient", "Low", "Medium", "High" }, Enum.GetNames<ConfidenceBand>());
}
