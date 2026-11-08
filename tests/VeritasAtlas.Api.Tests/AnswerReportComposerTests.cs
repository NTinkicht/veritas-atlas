using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class AnswerReportComposerTests
{
    private const string T = "t1";
    private static ServeCitation Cit(string r, FreshnessState? f = FreshnessState.Current) => new(T, r, true, f);
    private static readonly IReadOnlyDictionary<string, string> Texts = new Dictionary<string, string> { ["a"] = "Alpha", ["b"] = "Beta" };

    private static ReportComposition Run(IReadOnlyList<ServeClaim> claims, IReadOnlyDictionary<string, string>? texts = null,
        IReadOnlyList<KnownContradiction>? k = null, IReadOnlySet<string>? disclosed = null,
        HumanVerificationState v = HumanVerificationState.Verified) =>
        AnswerReportComposer.Compose(T, claims, texts ?? Texts, k, disclosed, v);

    [TestMethod]
    public void ServedAnswer_ListsClaimsAndCitationsInDeterministicOrder()
    {
        var r = Run([new("b", true, null, [Cit("ev1:z"), Cit("ev1:a")]), new("a", true, null, [Cit("ev1:m")])]);
        Assert.AreEqual(ServeDecision.Serve, r.Verdict.Decision);
        Assert.AreEqual("verified", r.Report!.HumanVerification);
        CollectionAssert.AreEqual(new[] { "a", "b" }, r.Report.Claims.Select(c => c.ClaimId).ToArray());
        CollectionAssert.AreEqual(new[] { "ev1:a", "ev1:z" }, r.Report.Claims[1].Citations.Select(c => c.EvidenceRef).ToArray());
        Assert.AreEqual("Alpha", r.Report.Claims[0].Text);
        Assert.AreEqual("current", r.Report.Claims[0].TemporalScope);
        Assert.IsFalse(r.Report.Claims[0].NonCurrentEvidence);
        Assert.AreEqual("current", r.Report.Claims[0].Citations[0].Freshness);
    }

    [TestMethod]
    public void HistoricalClaim_DisclosesAsOfDateAndNonCurrentStatus()
    {
        var asOf = new DateOnly(2020, 1, 1);
        var r = Run([new("a", false, asOf, [Cit("ev1:a", FreshnessState.Stale)])]);
        var c = r.Report!.Claims.Single();
        Assert.AreEqual("historical", c.TemporalScope);
        Assert.AreEqual(asOf, c.HistoricalAsOf);
        Assert.IsTrue(c.NonCurrentEvidence);
        Assert.AreEqual("stale", c.Citations.Single().Freshness);
    }

    [TestMethod]
    public void BlockedAnswer_ReturnsNoReportAndNoClaimText()
    {
        var r = Run([new("a", true, null, [Cit("ev1:a", FreshnessState.Stale)])]);
        Assert.AreEqual(ServeDecision.Block, r.Verdict.Decision);
        Assert.IsNull(r.Report);
        Assert.IsNull(Run([new("a", true, null, [Cit("ev1:a")])], v: HumanVerificationState.Unreviewed).Report);
    }

    [TestMethod]
    public void MissingOrBlankClaimText_FailsClosed()
    {
        foreach (var texts in new IReadOnlyDictionary<string, string>?[] { new Dictionary<string, string>(), new Dictionary<string, string> { ["a"] = " " } })
        {
            var r = Run([new("a", true, null, [Cit("ev1:a")])], texts);
            Assert.AreEqual(ServeDecision.Block, r.Verdict.Decision);
            Assert.IsNull(r.Report);
            CollectionAssert.AreEqual(new[] { ServeBlockers.ClaimTextMissing }, r.Verdict.Blockers.Select(b => b.Code).ToArray());
        }
    }

    [TestMethod]
    public void DisclosedContradictions_AreListedOnlyForClaimsInTheReport()
    {
        var k = new KnownContradiction[]
        {
            new("k2", ["a", "elsewhere"]), new("k1", ["b", "a", "a"]), new("k3", ["elsewhere"]),
        };
        var r = Run([new("a", true, null, [Cit("ev1:a")]), new("b", true, null, [Cit("ev1:b")])], k: k,
            disclosed: new HashSet<string> { "k1", "k2" });
        CollectionAssert.AreEqual(new[] { "k1", "k2" }, r.Report!.Contradictions.Select(x => x.ContradictionId).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b" }, r.Report.Contradictions[0].ClaimIds.ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, r.Report.Contradictions[1].ClaimIds.ToArray());
    }

    [TestMethod]
    public void UndisclosedContradiction_BlocksTheReport()
    {
        var r = Run([new("a", true, null, [Cit("ev1:a")])], k: [new("k1", ["a"])]);
        Assert.IsNull(r.Report);
        Assert.IsTrue(r.Verdict.Blockers.Any(b => b.Code == ServeBlockers.ContradictionNotDisclosed));
    }
}
