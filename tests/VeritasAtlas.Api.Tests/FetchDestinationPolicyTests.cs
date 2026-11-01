using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-04 slice 1 (#111): independent-fetch destination policy vectors —
/// allow-list, scheme/port, credentials, forbidden resolved ranges (SSRF and DNS
/// rebinding), redirect re-validation and bound, jurisdiction and policy-version drift.
/// </summary>
[TestClass]
public sealed class FetchDestinationPolicyTests
{
    private static readonly SourceFetchPolicy Policy = new(
        PolicyVersion: "registry-v3",
        Jurisdiction: "us-federal",
        SourceType: "court_record",
        AllowedHosts: ["records.example.gov", "mirror.example.gov"],
        AllowedSchemes: ["https"],
        AllowedPorts: [443],
        MaxRedirects: 2);

    private static readonly FetchExpectation Expected = new("registry-v3", "us-federal", "court_record");

    private static readonly IPAddress Public = IPAddress.Parse("93.184.216.34");

    private static FetchHop Hop(string? uri, params string[] addresses) =>
        new(uri, addresses.Select(IPAddress.Parse).ToArray());

    private static string? Reason(FetchHop hop) =>
        FetchDestinationGuard.EvaluateHop(Policy, Expected, hop).Reason;

    [TestMethod]
    public void AdmitsAllowListedHttpsHostWithPublicAddresses()
    {
        var decision = FetchDestinationGuard.EvaluateHop(
            Policy, Expected, Hop("https://RECORDS.example.gov/case/1?id=2", "93.184.216.34", "2606:2800:220:1::1"));
        Assert.IsTrue(decision.Admitted);
        Assert.IsNull(decision.Reason);
        Assert.AreEqual("https://records.example.gov/case/1?id=2", decision.FinalUri);
    }

    [TestMethod]
    public void DeniesClassificationAndPolicyDrift()
    {
        var hop = Hop("https://records.example.gov/", "93.184.216.34");
        Assert.AreEqual("policy_version_drift",
            FetchDestinationGuard.EvaluateHop(Policy, Expected with { PolicyVersion = "registry-v2" }, hop).Reason);
        Assert.AreEqual("wrong_jurisdiction",
            FetchDestinationGuard.EvaluateHop(Policy, Expected with { Jurisdiction = "us-ca" }, hop).Reason);
        Assert.AreEqual("wrong_source_type",
            FetchDestinationGuard.EvaluateHop(Policy, Expected with { SourceType = "news" }, hop).Reason);
        Assert.AreEqual("policy_version_drift", FetchDestinationGuard.EvaluateHop(null, Expected, hop).Reason);
        Assert.AreEqual("policy_version_drift", FetchDestinationGuard.EvaluateHop(Policy, null, hop).Reason);
    }

    [TestMethod]
    [DataRow(null, "invalid_uri")]
    [DataRow("", "invalid_uri")]
    [DataRow("/relative/path", "invalid_uri")]
    [DataRow("http://records.example.gov/", "scheme_not_allowed")]
    [DataRow("ftp://records.example.gov/", "scheme_not_allowed")]
    [DataRow("file:///etc/passwd", "invalid_uri")]
    [DataRow("https://user:pass@records.example.gov/", "credentials_in_uri")]
    [DataRow("https://records.example.gov@evil.example/", "credentials_in_uri")]
    [DataRow("https://evil.example/", "host_not_allowed")]
    [DataRow("https://sub.records.example.gov/", "host_not_allowed")]
    [DataRow("https://records.example.gov.evil.example/", "host_not_allowed")]
    [DataRow("https://records.example.gov:8443/", "port_not_allowed")]
    public void DeniesUriOutsideThePolicy(string? uri, string reason) =>
        Assert.AreEqual(reason, Reason(Hop(uri, "93.184.216.34")));

    [TestMethod]
    public void AdmitsHostWithTrailingDotAndExplicitDefaultPort() =>
        Assert.IsNull(Reason(Hop("https://records.example.gov.:443/x", "93.184.216.34")));

    [TestMethod]
    public void DeniesUnresolvedDestination()
    {
        Assert.AreEqual("unresolved_destination", Reason(Hop("https://records.example.gov/")));
        Assert.AreEqual("unresolved_destination",
            Reason(new FetchHop("https://records.example.gov/", null)));
    }

    [TestMethod]
    [DataRow("0.0.0.0")]
    [DataRow("0.1.2.3")]
    [DataRow("10.0.0.1")]
    [DataRow("127.0.0.1")]
    [DataRow("127.255.255.254")]
    [DataRow("100.64.0.1")]
    [DataRow("100.127.255.255")]
    [DataRow("169.254.169.254")]
    [DataRow("172.16.0.1")]
    [DataRow("172.31.255.255")]
    [DataRow("192.168.1.1")]
    [DataRow("224.0.0.1")]
    [DataRow("240.0.0.1")]
    [DataRow("255.255.255.255")]
    [DataRow("::")]
    [DataRow("::1")]
    [DataRow("::127.0.0.1")]
    [DataRow("::ffff:127.0.0.1")]
    [DataRow("::ffff:169.254.169.254")]
    [DataRow("64:ff9b::a00:1")]
    [DataRow("2002:c0a8:0101::1")]
    [DataRow("fc00::1")]
    [DataRow("fd12:3456::1")]
    [DataRow("fe80::1")]
    [DataRow("fec0::1")]
    [DataRow("ff02::1")]
    public void DeniesForbiddenResolvedAddress(string address)
    {
        Assert.IsTrue(FetchDestinationGuard.IsForbiddenAddress(IPAddress.Parse(address)));
        Assert.AreEqual("forbidden_address", Reason(Hop("https://records.example.gov/", address)));
    }

    [TestMethod]
    [DataRow("93.184.216.34")]
    [DataRow("100.63.255.255")]
    [DataRow("100.128.0.0")]
    [DataRow("172.15.255.255")]
    [DataRow("172.32.0.0")]
    [DataRow("169.253.0.1")]
    [DataRow("223.255.255.255")]
    [DataRow("::ffff:93.184.216.34")]
    [DataRow("64:ff9b::5db8:d822")]
    [DataRow("2002:5db8:d822::1")]
    [DataRow("2606:2800:220:1::1")]
    public void AdmitsPublicResolvedAddress(string address)
    {
        Assert.IsFalse(FetchDestinationGuard.IsForbiddenAddress(IPAddress.Parse(address)));
        Assert.IsNull(Reason(Hop("https://records.example.gov/", address)));
    }

    [TestMethod]
    public void DeniesWhenAnyResolvedAddressIsForbidden() =>
        Assert.AreEqual("forbidden_address",
            Reason(Hop("https://records.example.gov/", "93.184.216.34", "10.1.2.3")));

    [TestMethod]
    public void RevalidatesEveryRedirectHopAndReturnsTheFinalUri()
    {
        var admitted = FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected,
        [
            Hop("https://records.example.gov/a", "93.184.216.34"),
            Hop("https://mirror.example.gov/b", "93.184.216.35"),
        ]);
        Assert.IsTrue(admitted.Admitted);
        Assert.AreEqual("https://mirror.example.gov/b", admitted.FinalUri);

        Assert.AreEqual("forbidden_address", FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected,
        [
            Hop("https://records.example.gov/a", "93.184.216.34"),
            Hop("https://mirror.example.gov/b", "169.254.169.254"),
        ]).Reason);
        Assert.AreEqual("host_not_allowed", FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected,
        [
            Hop("https://records.example.gov/a", "93.184.216.34"),
            Hop("https://evil.example/b", "93.184.216.34"),
        ]).Reason);
        Assert.AreEqual("scheme_not_allowed", FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected,
        [
            Hop("https://records.example.gov/a", "93.184.216.34"),
            Hop("http://records.example.gov/a", "93.184.216.34"),
        ]).Reason);
    }

    [TestMethod]
    public void DeniesHttpsToHttpDowngradeInsideAnApprovedPlaintextExceptionChain()
    {
        var plaintextOk = Policy with { AllowedSchemes = ["https", "http"], AllowedPorts = [443, 80], PlaintextHttpException = true };
        var secure = Hop("https://records.example.gov/a", "93.184.216.34");
        var plain = Hop("http://records.example.gov/a", "93.184.216.34");

        Assert.AreEqual("scheme_downgrade",
            FetchDestinationGuard.EvaluateRedirectChain(plaintextOk, Expected, [secure, plain]).Reason);
        Assert.AreEqual("scheme_downgrade",
            FetchDestinationGuard.EvaluateRedirectChain(plaintextOk, Expected, [secure, secure, plain]).Reason);

        // Upgrade and same-scheme chains stay admitted.
        var upgrade = FetchDestinationGuard.EvaluateRedirectChain(plaintextOk, Expected, [plain, secure]);
        Assert.IsTrue(upgrade.Admitted);
        Assert.AreEqual("https://records.example.gov/a", upgrade.FinalUri);
        Assert.IsTrue(FetchDestinationGuard.EvaluateRedirectChain(plaintextOk, Expected, [plain, plain]).Admitted);

        // A per-hop denial keeps priority over the downgrade reason.
        Assert.AreEqual("forbidden_address", FetchDestinationGuard.EvaluateRedirectChain(plaintextOk, Expected,
            [secure, Hop("http://records.example.gov/a", "10.0.0.1")]).Reason);
        // Without the exception the plaintext hop is still a scheme denial.
        Assert.AreEqual("scheme_not_allowed", FetchDestinationGuard.EvaluateRedirectChain(
            plaintextOk with { PlaintextHttpException = false }, Expected, [secure, plain]).Reason);
    }

    [TestMethod]
    public void BoundsTheRedirectCountAndRejectsEmptyChains()
    {
        var hop = Hop("https://records.example.gov/", "93.184.216.34");
        Assert.IsTrue(FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected, [hop, hop, hop]).Admitted);
        Assert.AreEqual("too_many_redirects",
            FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected, [hop, hop, hop, hop]).Reason);
        Assert.AreEqual("too_many_redirects",
            FetchDestinationGuard.EvaluateRedirectChain(Policy with { MaxRedirects = -1 }, Expected, [hop]).Reason);
        Assert.AreEqual("invalid_uri", FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected, []).Reason);
        Assert.AreEqual("invalid_uri", FetchDestinationGuard.EvaluateRedirectChain(Policy, Expected, null).Reason);
        Assert.AreEqual("wrong_jurisdiction", FetchDestinationGuard.EvaluateRedirectChain(
            Policy, Expected with { Jurisdiction = "eu" }, [hop]).Reason);
    }

    [TestMethod]
    public void DeniesWhenThePolicyAllowsNothing()
    {
        var hop = Hop("https://records.example.gov/", "93.184.216.34");
        Assert.AreEqual("scheme_not_allowed",
            FetchDestinationGuard.EvaluateHop(Policy with { AllowedSchemes = [] }, Expected, hop).Reason);
        Assert.AreEqual("scheme_not_allowed",
            FetchDestinationGuard.EvaluateHop(Policy with { AllowedSchemes = ["ftp"] }, Expected,
                Hop("ftp://records.example.gov/", "93.184.216.34")).Reason);
        Assert.AreEqual("host_not_allowed",
            FetchDestinationGuard.EvaluateHop(Policy with { AllowedHosts = [] }, Expected, hop).Reason);
        Assert.AreEqual("port_not_allowed",
            FetchDestinationGuard.EvaluateHop(Policy with { AllowedPorts = [] }, Expected, hop).Reason);
    }
}
