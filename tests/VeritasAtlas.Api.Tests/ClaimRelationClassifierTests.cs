using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class ClaimRelationClassifierTests
{
    private static StructuredClaim C(string id, string value = "5", bool neg = false, string subject = "Acme Ltd",
        string? key = "ent-1", string pred = "employees", DateOnly? asOf = null, string tenant = "t1") =>
        new(tenant, id, "ev1:" + id, "src-" + id, subject, key, pred, neg, value, asOf);

    private static ClaimRelationResult K(StructuredClaim a, StructuredClaim b) => ClaimRelationClassifier.Classify(a, b);
    private static readonly DateOnly D1 = new(2024, 1, 1), D2 = new(2025, 1, 1);

    [TestMethod]
    public void MinimalPair_SameValue_Agrees_WithAttribution()
    {
        var r = K(C("a"), C("b", subject: "  ACME   ltd "));
        Assert.AreEqual(ClaimRelation.Agreeing, r.Relation);
        Assert.AreEqual("ev1:a", r.FirstEvidenceRef);
        Assert.AreEqual("src-b", r.SecondSourceId);
    }

    [TestMethod]
    public void Negation_SameValue_Conflicts() =>
        Assert.AreEqual(ClaimRelationReasons.PolarityConflict, K(C("a", asOf: D1), C("b", neg: true, asOf: D1)).Reason);

    [TestMethod]
    public void Negation_DifferentDates_IsSupersession_NotConflict_BothOrders()
    {
        foreach (var (x, y) in new[] { (C("old", asOf: D1), C("new", neg: true, asOf: D2)), (C("new", neg: true, asOf: D2), C("old", asOf: D1)) })
        {
            var r = K(x, y);
            Assert.AreEqual(ClaimRelation.Superseded, r.Relation);
            Assert.AreEqual("old", r.SupersededClaimId);
            Assert.AreEqual("new", r.SupersedingClaimId);
        }
        Assert.AreEqual(ClaimRelation.Unresolved, K(C("a", asOf: D1), C("b", neg: true)).Relation);
    }

    [TestMethod]
    public void SameAsOf_DifferentValue_Conflicts() =>
        Assert.AreEqual(ClaimRelation.Conflicting, K(C("a", "5", asOf: D1), C("b", "9", asOf: D1)).Relation);

    [TestMethod]
    public void DifferentDates_LaterSupersedes_RegardlessOfArgumentOrder()
    {
        var r = K(C("new", "9", asOf: D2), C("old", "5", asOf: D1));
        Assert.AreEqual(ClaimRelation.Superseded, r.Relation);
        Assert.AreEqual("old", r.SupersededClaimId);
        Assert.AreEqual("new", r.SupersedingClaimId);
    }

    [TestMethod]
    public void MissingDate_DifferentValue_IsUnresolved_NotConflict() =>
        Assert.AreEqual(ClaimRelation.Unresolved, K(C("a", "5", asOf: D1), C("b", "9")).Relation);

    [TestMethod]
    public void EntityCollision_SameName_DifferentKey_IsUnrelated() =>
        Assert.AreEqual(ClaimRelation.Unrelated, K(C("a", "5"), C("b", "9", key: "ent-2")).Relation);

    [TestMethod]
    public void UnresolvedEntity_IsUnresolved() =>
        Assert.AreEqual(ClaimRelation.Unresolved, K(C("a", key: null), C("b")).Relation);

    [TestMethod]
    public void FalsePositives_DifferentSubjectOrPredicate_AreUnrelated()
    {
        Assert.AreEqual(ClaimRelation.Unrelated, K(C("a"), C("b", "9", subject: "Other Co")).Relation);
        Assert.AreEqual(ClaimRelation.Unrelated, K(C("a"), C("b", "9", pred: "revenue")).Relation);
    }

    [TestMethod]
    public void NegationOfDifferentValue_IsNotAContradiction()
    {
        var both = K(C("a", "5", neg: true), C("b", "9", neg: true));
        Assert.AreEqual(ClaimRelation.Unresolved, both.Relation);
        Assert.AreEqual(ClaimRelationReasons.BothNegatedDifferentValue, both.Reason);
        var opp = K(C("a", "5", neg: true), C("b", "9"));
        Assert.AreEqual(ClaimRelation.Unresolved, opp.Relation);
        Assert.AreEqual(ClaimRelationReasons.OppositePolarityDifferentValue, opp.Reason);
    }

    [TestMethod]
    public void CrossTenant_AndSelfComparison_Throw()
    {
        Assert.ThrowsExactly<ArgumentException>(() => K(C("a"), C("b", tenant: "t2")));
        Assert.ThrowsExactly<ArgumentException>(() => K(C("a", tenant: ""), C("b", tenant: "")));
        Assert.ThrowsExactly<ArgumentException>(() => K(C("a"), C("a")));
    }
}
