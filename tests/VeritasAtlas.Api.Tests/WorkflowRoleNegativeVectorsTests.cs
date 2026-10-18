using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// Deterministic role-action matrix and publication-transition vectors for the
/// existing workflow policy (VER-P0-01). These tests pin current policy so any
/// widening fails; they do not grant publication authority or substitute for
/// tenant-scoped API tests.
/// </summary>
[TestClass]
public sealed class WorkflowRoleNegativeVectorsTests
{
    private static readonly string[] AllRoles = { "anonymous", "operator", "reviewer", "publisher", "admin" };

    private static readonly string[] ReviewerAndAbove = { "reviewer", "publisher", "admin" };

    private static readonly Dictionary<string, string[]> ExpectedRoleMatrix = new(StringComparer.Ordinal)
    {
        ["SubmitCase"] = new[] { "operator", "reviewer", "publisher", "admin" },
        ["ApproveCase"] = ReviewerAndAbove,
        ["RejectCase"] = ReviewerAndAbove,
        ["ResolveContradiction"] = ReviewerAndAbove,
        ["CompleteReview"] = ReviewerAndAbove,
        ["SendClaimToReview"] = ReviewerAndAbove,
        ["ReturnClaimForEdit"] = ReviewerAndAbove,
        ["EscalateContradiction"] = ReviewerAndAbove,
        ["ReopenReview"] = ReviewerAndAbove,
        ["PreparePublication"] = ReviewerAndAbove,
        ["PublishCase"] = ReviewerAndAbove,
        ["HoldCase"] = ReviewerAndAbove,
        ["SeedLifecycle"] = new[] { "operator", "reviewer", "publisher", "admin" },
    };

    private static readonly string[] CaseStatuses =
    {
        "Open", "Draft", "InReview", "Approved", "Rejected", "ReadyForPublication", "OnHold", "Published",
    };

    // Statuses from which a case may become Published: each one is reachable only
    // after the case has entered review. Unreviewed or rejected cases are excluded.
    private static readonly string[] ReviewedPublicationSources =
    {
        "InReview", "Approved", "ReadyForPublication", "OnHold", "Published",
    };

    private readonly WorkflowIntegrityService _policy = new();

    [TestMethod]
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
    public void PolicyExposesExactlyTheExpectedGovernedActions()
    {
        var actual = _policy.GetRolePolicies();

        CollectionAssert.AreEquivalent(
            ExpectedRoleMatrix.Keys.ToArray(),
            actual.Keys.ToArray(),
            "Governed workflow actions changed; update the role matrix deliberately.");

        foreach (var (action, roles) in ExpectedRoleMatrix)
        {
            CollectionAssert.AreEquivalent(
                roles,
                actual[action].Select(role => role.ToLowerInvariant()).ToArray(),
                $"Role policy for '{action}' changed.");
        }
    }

    [TestMethod]
    public void EveryRoleActionPairMatchesTheMatrix()
    {
        foreach (var (action, allowedRoles) in ExpectedRoleMatrix)
        {
            foreach (var role in AllRoles)
            {
                if (allowedRoles.Contains(role))
                {
                    _policy.EnsureRoleAllowed(action, role);
                }
                else
                {
                    Assert.ThrowsExactly<UnauthorizedAccessException>(
                        () => _policy.EnsureRoleAllowed(action, role),
                        $"'{role}' must not execute '{action}'.");
                }
            }
        }
    }

    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("guest")]
    [DataRow("publisher-admin")]
    [DataRow("viewer")]
    public void MissingOrUnknownRolesCannotExecuteAnyGovernedAction(string? role)
    {
        foreach (var action in ExpectedRoleMatrix.Keys)
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(
                () => _policy.EnsureRoleAllowed(action, role),
                $"Role '{role}' must not execute '{action}'.");
        }
    }

    [TestMethod]
    public void RoleMatchingIsCaseAndWhitespaceInsensitiveWithoutWideningOperator()
    {
        _policy.EnsureRoleAllowed("PublishCase", " Publisher ");
        _policy.EnsureRoleAllowed("ApproveCase", "REVIEWER");
        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            _policy.EnsureRoleAllowed("PublishCase", " OPERATOR "));
    }

    [TestMethod]
    public void ReviewAndPublicationRequireDistinctExistingRoles()
    {
        _policy.EnsureRoleAllowed("SubmitCase", "operator");
        _policy.EnsureRoleAllowed("ApproveCase", "reviewer");
        _policy.EnsureRoleAllowed("PublishCase", "publisher");
        _policy.EnsureTransitionAllowed("Case", "Open", "InReview");
        _policy.EnsureTransitionAllowed("Case", "InReview", "Approved");
        _policy.EnsureTransitionAllowed("Case", "Approved", "ReadyForPublication");
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

    [TestMethod]
    public void MissingCurrentStatusIsTreatedAsUnreviewedOpen()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _policy.EnsureTransitionAllowed("Case", null, "Published"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _policy.EnsureTransitionAllowed("Case", "  ", "Published"));
        _policy.EnsureTransitionAllowed("Case", null, "InReview");
    }

    [TestMethod]
    public void PublishedIsReachableOnlyFromReviewedStatuses()
    {
        foreach (var source in CaseStatuses)
        {
            if (ReviewedPublicationSources.Contains(source))
            {
                _policy.EnsureTransitionAllowed("Case", source, "Published");
            }
            else
            {
                Assert.ThrowsExactly<InvalidOperationException>(
                    () => _policy.EnsureTransitionAllowed("Case", source, "Published"),
                    $"Case must not publish from '{source}'.");
            }
        }
    }

    [TestMethod]
    public void UnreviewedCasesCanOnlyEnterReview()
    {
        foreach (var source in new[] { "Open", "Draft", "Rejected" })
        {
            foreach (var target in CaseStatuses)
            {
                if (target == "InReview")
                {
                    _policy.EnsureTransitionAllowed("Case", source, target);
                }
                else
                {
                    Assert.ThrowsExactly<InvalidOperationException>(
                        () => _policy.EnsureTransitionAllowed("Case", source, target),
                        $"Case must not move from '{source}' to '{target}'.");
                }
            }
        }
    }

    [TestMethod]
    public void PublishedCasesCannotBeReopenedOrRetracted()
    {
        foreach (var target in CaseStatuses.Where(status => status != "Published"))
        {
            Assert.ThrowsExactly<InvalidOperationException>(
                () => _policy.EnsureTransitionAllowed("Case", "Published", target),
                $"Published case must not move to '{target}'.");
        }
    }

    [TestMethod]
    [DataRow("Retracted")]
    [DataRow("Archived")]
    [DataRow("published-draft")]
    public void UnknownCaseStatusesFailClosed(string source)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _policy.EnsureTransitionAllowed("Case", source, "Published"));
    }
}
