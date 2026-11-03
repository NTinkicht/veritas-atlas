using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class AnswerServeGateTests
{
    private const string T = "t1";
    private static ServeCitation Cit(string r = "ev1:a", FreshnessState? f = FreshnessState.Current, bool ok = true, string t = T) => new(t, r, ok, f);
    private static ServeClaim Claim(string id = "c1", bool cur = true, DateOnly? asOf = null, params ServeCitation[] cs) =>
        new(id, cur, asOf, cs.Length == 0 ? [Cit()] : cs);
    private static ServeResult Run(IReadOnlyList<ServeClaim>? claims, IReadOnlyList<KnownContradiction>? k = null,
        IReadOnlySet<string>? disclosed = null, HumanVerificationState v = HumanVerificationState.Verified, string tenant = T) =>
        AnswerServeGate.Evaluate(tenant, claims, k, disclosed, v);
    private static string[] Codes(ServeResult r) => r.Blockers.Select(x => x.Code).ToArray();

    [TestMethod]
    public void CleanAnswer_Serves_WithNoBlockers()
    {
        var r = Run([Claim()]);
        Assert.AreEqual(ServeDecision.Serve, r.Decision);
        Assert.AreEqual(0, r.Blockers.Count);
    }

    [TestMethod]
    public void MissingTenant_Blocks() =>
        CollectionAssert.AreEqual(new[] { ServeBlockers.TenantMissing }, Codes(Run([Claim()], tenant: " ")));

    [TestMethod]
    public void NoClaims_NullOrEmpty_Blocks()
    {
        Assert.IsTrue(Codes(Run(null)).Contains(ServeBlockers.NoClaims));
        Assert.IsTrue(Codes(Run([])).Contains(ServeBlockers.NoClaims));
    }

    [TestMethod]
    public void ClaimWithoutCitation_Blocks()
    {
        Assert.IsTrue(Codes(Run([new ServeClaim("c1", true, null, null)])).Contains(ServeBlockers.ClaimWithoutCitation));
        Assert.IsTrue(Codes(Run([new ServeClaim("c1", true, null, [])])).Contains(ServeBlockers.ClaimWithoutCitation));
    }

    [TestMethod]
    public void BlankDuplicateUnverifiedAndCrossTenantCitations_Block()
    {
        Assert.IsTrue(Codes(Run([Claim(cs: Cit(" "))])).Contains(ServeBlockers.CitationBlankRef));
        Assert.IsTrue(Codes(Run([Claim(cs: [Cit("ev1:a"), Cit("ev1:a")])])).Contains(ServeBlockers.CitationDuplicateRef));
        Assert.IsTrue(Codes(Run([Claim(cs: Cit(ok: false))])).Contains(ServeBlockers.CitationNotVerified));
        Assert.IsTrue(Codes(Run([Claim(cs: Cit(t: "other"))])).Contains(ServeBlockers.CitationCrossTenant));
    }

    [TestMethod]
    [DataRow(FreshnessState.Stale)]
    [DataRow(FreshnessState.Unknown)]
    [DataRow(FreshnessState.Superseded)]
    [DataRow(null)]
    public void CurrentStateClaim_WithNonCurrentOrUnevaluatedEvidence_Blocks(FreshnessState? f) =>
        Assert.IsTrue(Codes(Run([Claim(cs: Cit(f: f))])).Contains(ServeBlockers.CurrentStateNotCurrent));

    [TestMethod]
    public void HistoricalClaim_NonCurrent_RequiresAsOfDate()
    {
        var stale = Cit(f: FreshnessState.Stale);
        Assert.IsTrue(Codes(Run([Claim(cur: false, cs: stale)])).Contains(ServeBlockers.HistoricalAsOfMissing));
        Assert.AreEqual(ServeDecision.Serve, Run([Claim(cur: false, asOf: new DateOnly(2020, 1, 1), cs: stale)]).Decision);
    }

    [TestMethod]
    public void UndisclosedContradiction_Blocks_DisclosedOrUnrelatedDoesNot()
    {
        var k = new[] { new KnownContradiction("k1", ["c1", "c2"]) };
        Assert.IsTrue(Codes(Run([Claim()], k)).Contains(ServeBlockers.ContradictionNotDisclosed));
        Assert.AreEqual(ServeDecision.Serve, Run([Claim()], k, new HashSet<string> { "k1" }).Decision);
        Assert.AreEqual(ServeDecision.Serve, Run([Claim("c9")], k).Decision);
    }

    [TestMethod]
    [DataRow(HumanVerificationState.Unreviewed)]
    [DataRow(HumanVerificationState.Rejected)]
    public void NotHumanVerified_Blocks(HumanVerificationState v) =>
        Assert.IsTrue(Codes(Run([Claim()], v: v)).Contains(ServeBlockers.HumanVerificationMissing));

    [TestMethod]
    public void Output_IsDeterministic_AndOrderedByClaimId()
    {
        var cs = new[] { Claim("b", cs: Cit(ok: false)), Claim("a", cs: Cit(ok: false)) };
        var r1 = Run(cs); var r2 = Run(cs.Reverse().ToArray());
        CollectionAssert.AreEqual(r1.Blockers.Select(x => x.ClaimId).ToArray(), r2.Blockers.Select(x => x.ClaimId).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "b" }, r1.Blockers.Where(x => x.ClaimId != null).Select(x => x.ClaimId!).ToArray());
    }
}
