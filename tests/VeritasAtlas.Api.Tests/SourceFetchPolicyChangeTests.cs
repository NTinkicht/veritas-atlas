using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-04 slice 4 (#117): source change policy vectors for replacing a registered
/// fetch policy — admission of the proposal, version bump and no reuse (registry version
/// drift), immutable classification (wrong jurisdiction/source type) and widening only
/// with explicit approval.
/// </summary>
[TestClass]
public sealed class SourceFetchPolicyChangeTests
{
    private static readonly string[] History = ["registry-v1", "registry-v2", "registry-v3"];

    private static readonly SourceFetchPolicy Current = new(
        PolicyVersion: "registry-v3",
        Jurisdiction: "us-federal",
        SourceType: "court_record",
        AllowedHosts: ["records.example.gov", "files.example.gov"],
        AllowedSchemes: ["https"],
        AllowedPorts: [443],
        MaxRedirects: 2);

    private static readonly SourceFetchPolicy Bumped = Current with { PolicyVersion = "registry-v4" };

    private static SourceFetchPolicyChangeResult Evaluate(
        SourceFetchPolicy? proposed, bool wideningApproved = false, SourceFetchPolicy? current = null) =>
        SourceFetchPolicyChange.Evaluate(current ?? Current, proposed, History, wideningApproved);

    private static void AssertDenied(SourceFetchPolicyChangeResult result, params string[] reasons)
    {
        Assert.IsFalse(result.Admitted);
        CollectionAssert.AreEqual(reasons, result.Reasons.ToArray());
    }

    [TestMethod]
    public void IdenticalPolicyIsAnAdmittedNoOpRegardlessOfSetOrder()
    {
        var result = Evaluate(Current with { AllowedHosts = ["FILES.example.gov", "records.example.gov"] });
        Assert.IsFalse(SourceFetchPolicyAdmission.Validate(Current with { AllowedHosts = ["FILES.example.gov"] }).Admitted);
        // Upper-case hosts never pass admission, so only the order-insensitive case is a no-op.
        AssertDenied(result, "invalid_allowed_host");

        var same = Evaluate(Current with { AllowedHosts = ["files.example.gov", "records.example.gov"] });
        Assert.IsTrue(same.Admitted);
        Assert.IsFalse(same.Changed);
        Assert.AreEqual(0, same.Reasons.Count);
        Assert.AreEqual(0, same.Widenings.Count);
    }

    [TestMethod]
    public void AdmitsANarrowingRevisionUnderANewVersionWithoutApproval()
    {
        var result = Evaluate(Bumped with { AllowedHosts = ["records.example.gov"], MaxRedirects = 0 });
        Assert.IsTrue(result.Admitted);
        Assert.IsTrue(result.Changed);
        Assert.AreEqual(0, result.Reasons.Count);
        Assert.AreEqual(0, result.Widenings.Count);
    }

    [TestMethod]
    public void AVersionOnlyBumpIsAdmittedAsAChange()
    {
        var result = Evaluate(Bumped);
        Assert.IsTrue(result.Admitted);
        Assert.IsTrue(result.Changed);
    }

    [TestMethod]
    public void DifferentContentUnderTheSameVersionIsDrift()
    {
        AssertDenied(Evaluate(Current with { AllowedHosts = ["records.example.gov"] }), "policy_version_not_bumped");
    }

    [TestMethod]
    public void AnEarlierVersionNameIsNeverReused()
    {
        AssertDenied(Evaluate(Current with { PolicyVersion = "registry-v1", MaxRedirects = 1 }), "policy_version_reused");
    }

    [TestMethod]
    public void JurisdictionAndSourceTypeCannotBeReclassifiedInPlace()
    {
        AssertDenied(Evaluate(Bumped with { Jurisdiction = "us-ca" }), "jurisdiction_change_forbidden");
        AssertDenied(Evaluate(Bumped with { SourceType = "statute" }), "source_type_change_forbidden");
        AssertDenied(
            Evaluate(Current with { Jurisdiction = "us-ca", SourceType = "statute" }),
            "policy_version_not_bumped", "jurisdiction_change_forbidden", "source_type_change_forbidden");
    }

    [TestMethod]
    public void EachWideningNeedsExplicitApproval()
    {
        var cases = new (SourceFetchPolicy Proposed, string Widening)[]
        {
            (Bumped with { AllowedHosts = ["records.example.gov", "files.example.gov", "new.example.gov"] }, "allowed_hosts_added"),
            (Bumped with { AllowedHosts = ["records.example.gov", "other.example.gov"] }, "allowed_hosts_added"),
            (Bumped with { AllowedPorts = [443, 8443] }, "allowed_ports_added"),
            (Bumped with { MaxRedirects = 3 }, "max_redirects_increased"),
        };
        foreach (var (proposed, widening) in cases)
        {
            var denied = Evaluate(proposed);
            AssertDenied(denied, "widening_requires_approval");
            CollectionAssert.AreEqual(new[] { widening }, denied.Widenings.ToArray(), widening);

            var approved = Evaluate(proposed, wideningApproved: true);
            Assert.IsTrue(approved.Admitted, widening);
            CollectionAssert.AreEqual(new[] { widening }, approved.Widenings.ToArray(), widening);
        }
    }

    [TestMethod]
    public void EnablingPlaintextHttpReportsBothWideningsInFixedOrder()
    {
        var proposed = Bumped with { AllowedSchemes = ["https", "http"], AllowedPorts = [443, 80], PlaintextHttpException = true };
        var denied = Evaluate(proposed);
        AssertDenied(denied, "widening_requires_approval");
        CollectionAssert.AreEqual(
            new[] { "allowed_schemes_added", "plaintext_http_enabled", "allowed_ports_added" },
            denied.Widenings.ToArray());
        Assert.IsTrue(Evaluate(proposed, wideningApproved: true).Admitted);
    }

    [TestMethod]
    public void ApprovalNeverOverridesVersionOrClassificationRules()
    {
        var result = Evaluate(Current with { Jurisdiction = "us-ca", MaxRedirects = 5 }, wideningApproved: true);
        AssertDenied(result, "policy_version_not_bumped", "jurisdiction_change_forbidden");
        CollectionAssert.AreEqual(new[] { "max_redirects_increased" }, result.Widenings.ToArray());
    }

    [TestMethod]
    public void TheProposalMustPassConnectorAdmission()
    {
        AssertDenied(Evaluate(Bumped with { AllowedHosts = ["10.0.0.1"] }, wideningApproved: true), "invalid_allowed_host");
        AssertDenied(Evaluate(Bumped with { AllowedSchemes = ["https", "http"], AllowedPorts = [443, 80] }, wideningApproved: true),
            "http_without_exception");
        AssertDenied(Evaluate(null), "policy_missing");
    }

    [TestMethod]
    public void MissingCurrentPolicyOrHistoryFailsClosed()
    {
        AssertDenied(SourceFetchPolicyChange.Evaluate(null, Bumped, History, wideningApproved: true), "current_policy_missing");
        AssertDenied(SourceFetchPolicyChange.Evaluate(Current, Bumped, null, wideningApproved: true), "invalid_version_history");
        AssertDenied(SourceFetchPolicyChange.Evaluate(Current, Bumped, [null!], wideningApproved: true), "invalid_version_history");
    }

    [TestMethod]
    public void AnInvalidLegacyCurrentPolicyTreatsEveryProposedEntryAsWidening()
    {
        var legacy = Current with { AllowedHosts = null!, AllowedPorts = null! };
        var result = Evaluate(Bumped, current: legacy);
        AssertDenied(result, "widening_requires_approval");
        CollectionAssert.AreEqual(new[] { "allowed_hosts_added", "allowed_ports_added" }, result.Widenings.ToArray());
    }

    [TestMethod]
    public void ResultsAreDeterministic()
    {
        var proposed = Current with { Jurisdiction = "us-ca", AllowedPorts = [443, 8443] };
        var first = Evaluate(proposed);
        var second = Evaluate(proposed);
        CollectionAssert.AreEqual(first.Reasons.ToArray(), second.Reasons.ToArray());
        CollectionAssert.AreEqual(first.Widenings.ToArray(), second.Widenings.ToArray());
        CollectionAssert.AreEqual(
            new[] { "policy_version_not_bumped", "jurisdiction_change_forbidden", "widening_requires_approval" },
            first.Reasons.ToArray());
    }
}
