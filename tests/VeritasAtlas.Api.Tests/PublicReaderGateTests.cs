using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class PublicReaderGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static PublicReaderFacts F(LifecycleState s = LifecycleState.Published, bool permit = true, bool restricted = false,
        bool superseded = false, DateTimeOffset? exp = null, bool fresh = false, EvidenceCurrency cur = EvidenceCurrency.Current, bool label = true) =>
        new(s, permit, restricted, superseded, exp, Now, fresh, cur, label);

    private static void Hidden(PublicReaderFacts f, string reason)
    {
        var d = PublicReaderGate.Evaluate(f);
        Assert.IsFalse(d.Visible);
        Assert.AreEqual(reason, d.InternalReason);
    }

    [TestMethod]
    public void Published_Permitted_Current_IsVisible()
    {
        var d = PublicReaderGate.Evaluate(F(exp: Now.AddDays(1), fresh: true));
        Assert.IsTrue(d.Visible);
        Assert.IsNull(d.InternalReason);
    }

    [TestMethod]
    public void EveryNonPublishedState_IsHidden()
    {
        foreach (var s in Enum.GetValues<LifecycleState>().Where(s => s != LifecycleState.Published))
            Hidden(F(s: s), PublicHiddenReasons.NotPublished);
    }

    [TestMethod]
    public void ApprovedButNotPublished_IsHidden() => Hidden(F(s: LifecycleState.Approved), PublicHiddenReasons.NotPublished);

    [TestMethod]
    public void PolicyDoesNotPermit_Restricted_Superseded_AreHidden()
    {
        Hidden(F(permit: false), PublicHiddenReasons.PublicDisplayNotPermitted);
        Hidden(F(restricted: true), PublicHiddenReasons.Restricted);
        Hidden(F(superseded: true), PublicHiddenReasons.Superseded);
    }

    [TestMethod]
    public void Expiry_IsExclusiveAtBoundary()
    {
        Hidden(F(exp: Now), PublicHiddenReasons.Expired);
        Hidden(F(exp: Now.AddSeconds(-1)), PublicHiddenReasons.Expired);
        Assert.IsTrue(PublicReaderGate.Evaluate(F(exp: Now.AddSeconds(1))).Visible);
    }

    [TestMethod]
    public void FreshnessRequired_NonCurrentOrUnknownEvidence_IsHidden_EvenWithLabel()
    {
        Hidden(F(fresh: true, cur: EvidenceCurrency.NonCurrent, label: true), PublicHiddenReasons.StaleCurrentState);
        Hidden(F(fresh: true, cur: EvidenceCurrency.Unknown, label: true), PublicHiddenReasons.StaleCurrentState);
    }

    [TestMethod]
    public void Historical_WithTruthfulLabel_StaysVisible_WithoutLabelIsHidden()
    {
        Assert.IsTrue(PublicReaderGate.Evaluate(F(fresh: false, cur: EvidenceCurrency.NonCurrent, label: true)).Visible);
        Hidden(F(fresh: false, cur: EvidenceCurrency.NonCurrent, label: false), PublicHiddenReasons.HistoricalLabelMissing);
        Hidden(F(fresh: false, cur: EvidenceCurrency.Unknown, label: false), PublicHiddenReasons.HistoricalLabelMissing);
    }

    [TestMethod]
    public void AnonymousOutcome_IsUniform_ForEveryHiddenCause()
    {
        Assert.AreEqual("not_found", PublicReaderDecision.AnonymousHiddenOutcome);
        var reasons = typeof(PublicHiddenReasons).GetFields().Select(x => (string)x.GetRawConstantValue()!).ToList();
        Assert.AreEqual(7, reasons.Count);
        Assert.IsFalse(reasons.Contains("not_found"));
    }

    [TestMethod]
    public void Projection_ExposesExactlyTheAllowList_NoInternalIdentifiers()
    {
        var names = typeof(PublicCitationProjection).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(
            new[] { "AsOf", "CitationLabel", "EffectiveDate", "FreshnessLabel", "Quote", "SourceName", "Title" }, names);
        foreach (var banned in new[] { "tenant", "id", "key", "digest", "hash", "reviewer", "author" })
            Assert.IsFalse(names.Any(n => n.Contains(banned, StringComparison.OrdinalIgnoreCase)), banned);
    }
}
