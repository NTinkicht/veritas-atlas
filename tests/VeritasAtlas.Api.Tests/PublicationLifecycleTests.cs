using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class PublicationLifecycleTests
{
    private static LifecycleRequest R(LifecycleState s, LifecycleAction a, LifecycleRole role, string actor = "u2", string author = "u1",
        string revTenant = "t1", string actTenant = "t1", bool pub = true) =>
        new(revTenant, actTenant, actor, role, author, s, a, pub);

    private static void Ok(LifecycleRequest r, LifecycleState expected)
    {
        var x = PublicationLifecycle.Transition(r);
        Assert.IsTrue(x.Allowed, x.DenialCode);
        Assert.AreEqual(expected, x.NewState);
        Assert.IsNull(x.DenialCode);
    }

    private static void Denied(LifecycleRequest r, string code)
    {
        var x = PublicationLifecycle.Transition(r);
        Assert.IsFalse(x.Allowed);
        Assert.IsNull(x.NewState);
        Assert.AreEqual(code, x.DenialCode);
    }

    [TestMethod]
    public void HappyPath_DraftToPublishedToWithdrawn()
    {
        Ok(R(LifecycleState.Draft, LifecycleAction.Submit, LifecycleRole.Analyst, actor: "u1"), LifecycleState.Submitted);
        Ok(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer), LifecycleState.Approved);
        Ok(R(LifecycleState.Approved, LifecycleAction.Publish, LifecycleRole.Publisher), LifecycleState.Published);
        Ok(R(LifecycleState.Published, LifecycleAction.Withdraw, LifecycleRole.Publisher), LifecycleState.Withdrawn);
    }

    [TestMethod]
    public void ChangesRequested_ReturnsToDraft_ByAuthorOnly()
    {
        Ok(R(LifecycleState.Submitted, LifecycleAction.RequestChanges, LifecycleRole.Reviewer), LifecycleState.ChangesRequested);
        Ok(R(LifecycleState.ChangesRequested, LifecycleAction.Revise, LifecycleRole.Analyst, actor: "u1"), LifecycleState.Draft);
        Denied(R(LifecycleState.ChangesRequested, LifecycleAction.Revise, LifecycleRole.Analyst, actor: "u9"), LifecycleDenials.NotAuthor);
    }

    [TestMethod]
    public void SelfApproval_IsDenied_ForApproveAndRequestChanges()
    {
        Denied(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer, actor: "u1"), LifecycleDenials.SelfApprovalDenied);
        Denied(R(LifecycleState.Submitted, LifecycleAction.RequestChanges, LifecycleRole.Reviewer, actor: "u1"), LifecycleDenials.SelfApprovalDenied);
    }

    [TestMethod]
    public void Approved_IsNotPublic_PublishNeedsExplicitPolicyPermit()
    {
        Denied(R(LifecycleState.Approved, LifecycleAction.Publish, LifecycleRole.Publisher, pub: false), LifecycleDenials.PublicDisplayNotPermitted);
        Ok(R(LifecycleState.Approved, LifecycleAction.Withdraw, LifecycleRole.Publisher, pub: false), LifecycleState.Withdrawn);
    }

    [TestMethod]
    public void CrossTenantAndBlankIds_FailClosed()
    {
        Denied(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer, actTenant: "t2"), LifecycleDenials.CrossTenant);
        Denied(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer, revTenant: " "), LifecycleDenials.TenantMissing);
        Denied(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer, actTenant: ""), LifecycleDenials.TenantMissing);
        Denied(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer, actor: ""), LifecycleDenials.ActorMissing);
        Denied(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer, author: ""), LifecycleDenials.ActorMissing);
    }

    [TestMethod]
    public void WrongRole_IsDenied_ForEveryAction()
    {
        Denied(R(LifecycleState.Draft, LifecycleAction.Submit, LifecycleRole.Reviewer, actor: "u1"), LifecycleDenials.RoleNotPermitted);
        Denied(R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Publisher), LifecycleDenials.RoleNotPermitted);
        Denied(R(LifecycleState.Submitted, LifecycleAction.RequestChanges, LifecycleRole.None), LifecycleDenials.RoleNotPermitted);
        Denied(R(LifecycleState.Approved, LifecycleAction.Publish, LifecycleRole.Reviewer), LifecycleDenials.RoleNotPermitted);
        Denied(R(LifecycleState.Published, LifecycleAction.Withdraw, LifecycleRole.Analyst), LifecycleDenials.RoleNotPermitted);
    }

    [TestMethod]
    public void OnlyTheSevenDefinedTransitionsAreAllowed_AndWithdrawnIsTerminal()
    {
        var allowed = new HashSet<(LifecycleState, LifecycleAction)>
        {
            (LifecycleState.Draft, LifecycleAction.Submit), (LifecycleState.Submitted, LifecycleAction.RequestChanges),
            (LifecycleState.Submitted, LifecycleAction.Approve), (LifecycleState.ChangesRequested, LifecycleAction.Revise),
            (LifecycleState.Approved, LifecycleAction.Publish), (LifecycleState.Approved, LifecycleAction.Withdraw),
            (LifecycleState.Published, LifecycleAction.Withdraw),
        };
        foreach (var s in Enum.GetValues<LifecycleState>())
            foreach (var a in Enum.GetValues<LifecycleAction>())
            {
                if (allowed.Contains((s, a))) continue;
                foreach (var role in Enum.GetValues<LifecycleRole>())
                    Denied(R(s, a, role, actor: "u1"), LifecycleDenials.InvalidTransition);
            }
    }

    [TestMethod]
    public void SameInputs_SameOutput()
    {
        var r = R(LifecycleState.Submitted, LifecycleAction.Approve, LifecycleRole.Reviewer);
        Assert.AreEqual(PublicationLifecycle.Transition(r), PublicationLifecycle.Transition(r));
    }
}
