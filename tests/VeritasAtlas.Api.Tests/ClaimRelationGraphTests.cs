using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class ClaimRelationGraphTests
{
    private static readonly DateOnly D1 = new(2024, 1, 1), D2 = new(2025, 1, 1);

    private static StructuredClaim C(string id, string value = "5", bool neg = false, string subject = "Acme Ltd",
        string? key = "ent-1", string pred = "employees", DateOnly? asOf = null, string tenant = "t1") =>
        new(tenant, id, "ev1:" + id, "src-" + id, subject, key, pred, neg, value, asOf);

    private static ClaimGraphStatus S(ClaimRelationGraph g, string id) => g.Nodes.Single(n => n.ClaimId == id).Status;

    [TestMethod]
    public void Agreeing_Pair_HasAttributedEdge_AndAgreeingNodes()
    {
        var g = ClaimRelationGraphBuilder.Build([C("a"), C("b")]);
        Assert.AreEqual(1, g.Edges.Count);
        Assert.AreEqual(ClaimRelation.Agreeing, g.Edges[0].Relation);
        Assert.AreEqual("ev1:a", g.Edges[0].FirstEvidenceRef);
        Assert.AreEqual("src-b", g.Edges[0].SecondSourceId);
        Assert.AreEqual(ClaimGraphStatus.Agreeing, S(g, "a"));
        Assert.AreEqual(ClaimGraphStatus.Agreeing, S(g, "b"));
    }

    [TestMethod]
    public void Conflict_MarksBothConflicted_AndThirdAgreeingClaimStaysConflictedIfLinked()
    {
        var g = ClaimRelationGraphBuilder.Build([C("a", "5", asOf: D1), C("b", "9", asOf: D1), C("c", "5", asOf: D1)]);
        Assert.AreEqual(ClaimGraphStatus.Conflicted, S(g, "a"));
        Assert.AreEqual(ClaimGraphStatus.Conflicted, S(g, "b"));
        Assert.AreEqual(ClaimGraphStatus.Conflicted, S(g, "c"));
        Assert.AreEqual(3, g.Edges.Count);
    }

    [TestMethod]
    public void Supersession_KeepsOlderClaimVisible_AsSuperseded()
    {
        var g = ClaimRelationGraphBuilder.Build([C("new", "9", asOf: D2), C("old", "5", asOf: D1)]);
        Assert.AreEqual(2, g.Nodes.Count);
        Assert.AreEqual(ClaimGraphStatus.Superseded, S(g, "old"));
        Assert.AreEqual(ClaimGraphStatus.Agreeing, S(g, "new"));
        Assert.AreEqual("old", g.Edges[0].SupersededClaimId);
    }

    [TestMethod]
    public void EntityCollision_ProducesNoEdge_AndUnresolvedEntityIsNeverConflicting()
    {
        var collision = ClaimRelationGraphBuilder.Build([C("a", "5", key: "ent-1"), C("b", "9", key: "ent-2")]);
        Assert.AreEqual(0, collision.Edges.Count);
        Assert.AreEqual(ClaimGraphStatus.Isolated, S(collision, "a"));

        var unresolved = ClaimRelationGraphBuilder.Build([C("a", "5", key: null, asOf: D1), C("b", "9", key: "ent-1", asOf: D1)]);
        Assert.AreEqual(ClaimRelation.Unresolved, unresolved.Edges.Single().Relation);
        Assert.AreEqual(ClaimGraphStatus.Unresolved, S(unresolved, "a"));
    }

    [TestMethod]
    public void BothNegatedDifferentValue_IsUnresolved_NotConflict()
    {
        var g = ClaimRelationGraphBuilder.Build([C("a", "5", neg: true, asOf: D1), C("b", "9", neg: true, asOf: D1)]);
        Assert.AreEqual(ClaimRelation.Unresolved, g.Edges.Single().Relation);
    }

    [TestMethod]
    public void Conflicted_TakesPrecedenceOverUnresolvedAndSuperseded()
    {
        var g = ClaimRelationGraphBuilder.Build([
            C("a", "5", asOf: D1), C("b", "9", asOf: D1), C("c", "7", asOf: D2), C("d", "1")]);
        Assert.AreEqual(ClaimGraphStatus.Conflicted, S(g, "a"));
        Assert.AreEqual(ClaimGraphStatus.Unresolved, S(g, "d"));
        Assert.AreEqual(ClaimGraphStatus.Unresolved, S(g, "c"));
    }

    [TestMethod]
    public void Output_IsIndependentOfInputOrder()
    {
        var claims = new[] { C("c", "5", asOf: D1), C("a", "9", asOf: D1), C("b", "5", asOf: D2), C("z", "1", subject: "Other") };
        var one = ClaimRelationGraphBuilder.Build(claims);
        var two = ClaimRelationGraphBuilder.Build(claims.Reverse().ToList());
        CollectionAssert.AreEqual(one.Nodes.ToList(), two.Nodes.ToList());
        CollectionAssert.AreEqual(one.Edges.ToList(), two.Edges.ToList());
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "z" }, one.Nodes.Select(n => n.ClaimId).ToArray());
    }

    [TestMethod]
    public void SingleClaim_IsIsolated()
    {
        var g = ClaimRelationGraphBuilder.Build([C("a")]);
        Assert.AreEqual(0, g.Edges.Count);
        Assert.AreEqual(ClaimGraphStatus.Isolated, S(g, "a"));
    }

    [TestMethod]
    public void FailsClosed_OnEmptyMixedTenantDuplicateBlankIdNullAndOversize()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ClaimRelationGraphBuilder.Build([]));
        Assert.ThrowsExactly<ArgumentException>(() => ClaimRelationGraphBuilder.Build([C("a"), C("b", tenant: "t2")]));
        Assert.ThrowsExactly<ArgumentException>(() => ClaimRelationGraphBuilder.Build([C("a", tenant: " ")]));
        Assert.ThrowsExactly<ArgumentException>(() => ClaimRelationGraphBuilder.Build([C("a"), C("a", "9")]));
        Assert.ThrowsExactly<ArgumentException>(() => ClaimRelationGraphBuilder.Build([C(" ")]));
        Assert.ThrowsExactly<ArgumentException>(() => ClaimRelationGraphBuilder.Build([C("a"), null!]));
        var many = Enumerable.Range(0, ClaimRelationGraphBuilder.MaxClaims + 1).Select(i => C("c" + i)).ToList();
        Assert.ThrowsExactly<ArgumentException>(() => ClaimRelationGraphBuilder.Build(many));
        Assert.AreEqual(ClaimRelationGraphBuilder.MaxClaims,
            ClaimRelationGraphBuilder.Build(many.Take(ClaimRelationGraphBuilder.MaxClaims).ToList()).Nodes.Count);
    }
}
