using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// Negative role vectors for the existing workflow policy. These tests do not
/// grant publication authority or substitute for tenant-scoped API tests.
/// </summary>
[TestClass]
public sealed class WorkflowRoleNegativeVectorsTests
{
    private readonly WorkflowIntegrityService _policy = new();

    [DataTestMethod]
    [DataRow("SubmitCase", "anonymous")]
    [DataRow("ApproveCase", "operator")]
    [DataRow("RejectCase", "operator")]
    [DataRow("CompleteReview", "operator")]
    [DataRow("PublishCase", "operator")]
    [DataRow("PublishCase", "anonymous")]
    [DataRow("PreparePublication", "operator")]
    public void UnprivilegedRolesCannotMutateGovernedWorkflow(string action, string role)
    {
        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            _policy.EnsureRoleAllowed(action, role));
    }

    [TestMethod]
    public void ReviewAndPublicationRequireDistinctExistingRoles()
    {
        _policy.EnsureRoleAllowed("SubmitCase", "operator");
        _policy.EnsureRoleAllowed("ApproveCase", "reviewer");
        _policy.EnsureRoleAllowed("PublishCase", "publisher");
        _policy.EnsureTransitionAllowed("Case", "Open", "InReview");
        _policy.EnsureTransitionAllowed("Case", "InReview", "Approved");
        _policy.EnsureTransitionAllowed("Case", "ReadyForPublication", "Published");
    }

    [TestMethod]
    public void PublicationCannotSkipAnUnreviewedOpenCase()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _policy.EnsureTransitionAllowed("Case", "Open", "Published"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _policy.EnsureTransitionAllowed("Case", "Draft", "Published"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _policy.EnsureTransitionAllowed("Case", "Rejected", "Published"));
    }
}
