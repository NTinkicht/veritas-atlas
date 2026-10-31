using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-04 slice 2 (#113): connector admission vectors for source fetch policies —
/// classification formats, exact DNS allow-list hosts (no IP literals, wildcards or
/// local-only names), schemes and the explicit plaintext-http exception, ports and the
/// redirect bound — plus the destination guard's matching http rule.
/// </summary>
[TestClass]
public sealed class SourceFetchPolicyAdmissionTests
{
    private static readonly SourceFetchPolicy Valid = new(
        PolicyVersion: "registry-v3",
        Jurisdiction: "us-federal",
        SourceType: "court_record",
        AllowedHosts: ["records.example.gov", "xn--bcher-kva.example"],
        AllowedSchemes: ["https"],
        AllowedPorts: [443],
        MaxRedirects: 2);

    private static IReadOnlyList<string> Reasons(SourceFetchPolicy? policy) =>
        SourceFetchPolicyAdmission.Validate(policy).Reasons;

    private static void AssertOnly(string reason, SourceFetchPolicy policy)
    {
        var result = SourceFetchPolicyAdmission.Validate(policy);
        Assert.IsFalse(result.Admitted);
        CollectionAssert.AreEqual(new[] { reason }, result.Reasons.ToArray());
    }

    [TestMethod]
    public void AdmitsAWellFormedPolicy()
    {
        var result = SourceFetchPolicyAdmission.Validate(Valid);
        Assert.IsTrue(result.Admitted);
        Assert.AreEqual(0, result.Reasons.Count);
        Assert.IsTrue(SourceFetchPolicyAdmission.Validate(Valid with
        {
            AllowedSchemes = ["https", "http"],
            AllowedPorts = [443, 80, 65535, 1],
            PlaintextHttpException = true,
            MaxRedirects = 10,
        }).Admitted);
        Assert.IsTrue(SourceFetchPolicyAdmission.Validate(Valid with { MaxRedirects = 0 }).Admitted);
    }

    [TestMethod]
    public void RejectsAMissingPolicy()
    {
        var result = SourceFetchPolicyAdmission.Validate(null);
        Assert.IsFalse(result.Admitted);
        CollectionAssert.AreEqual(new[] { "policy_missing" }, result.Reasons.ToArray());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" registry-v3")]
    [DataRow("Registry-V3")]
    [DataRow("registry v3")]
    [DataRow("registry-v3\n")]
    [DataRow("-registry")]
    public void RejectsMalformedPolicyVersions(string version) =>
        AssertOnly("invalid_policy_version", Valid with { PolicyVersion = version });

    [TestMethod]
    public void RejectsMalformedClassification()
    {
        AssertOnly("invalid_policy_version", Valid with { PolicyVersion = null! });
        AssertOnly("invalid_policy_version", Valid with { PolicyVersion = "v" + new string('1', 64) });
        AssertOnly("invalid_jurisdiction", Valid with { Jurisdiction = "" });
        AssertOnly("invalid_jurisdiction", Valid with { Jurisdiction = "US-Federal" });
        AssertOnly("invalid_jurisdiction", Valid with { Jurisdiction = "us_federal" });
        AssertOnly("invalid_jurisdiction", Valid with { Jurisdiction = "us-federal\n" });
        AssertOnly("invalid_source_type", Valid with { SourceType = "" });
        AssertOnly("invalid_source_type", Valid with { SourceType = "1court" });
        AssertOnly("invalid_source_type", Valid with { SourceType = "court-record" });
        Assert.IsTrue(SourceFetchPolicyAdmission.Validate(Valid with { PolicyVersion = "v" + new string('1', 63) }).Admitted);
    }

    [TestMethod]
    [DataRow("10.0.0.1")]
    [DataRow("93.184.216.34")]
    [DataRow("::1")]
    [DataRow("[2606:2800:220:1::1]")]
    [DataRow("*.example.gov")]
    [DataRow("RECORDS.example.gov")]
    [DataRow("records.example.gov.")]
    [DataRow("records..example.gov")]
    [DataRow("-records.example.gov")]
    [DataRow("records-.example.gov")]
    [DataRow("records_x.example.gov")]
    [DataRow("records.example.gov:443")]
    [DataRow("https://records.example.gov")]
    [DataRow("bücher.example")]
    [DataRow("localhost")]
    [DataRow("intranet")]
    [DataRow("")]
    [DataRow(" records.example.gov")]
    public void RejectsHostsThatAreNotExactLowercaseDnsNames(string host) =>
        AssertOnly("invalid_allowed_host", Valid with { AllowedHosts = [host] });

    [TestMethod]
    public void BoundsHostAndLabelLength()
    {
        var label63 = new string('a', 63);
        Assert.IsTrue(SourceFetchPolicyAdmission.Validate(Valid with { AllowedHosts = [label63 + ".gov"] }).Admitted);
        AssertOnly("invalid_allowed_host", Valid with { AllowedHosts = [label63 + "a.gov"] });
        var host253 = string.Join('.', Enumerable.Repeat(label63, 3)) + "." + new string('b', 61);
        Assert.AreEqual(253, host253.Length);
        Assert.IsTrue(SourceFetchPolicyAdmission.Validate(Valid with { AllowedHosts = [host253] }).Admitted);
        AssertOnly("invalid_allowed_host", Valid with { AllowedHosts = [host253 + "b"] });
        AssertOnly("invalid_allowed_host", Valid with { AllowedHosts = [null!] });
    }

    [TestMethod]
    [DataRow("dev.localhost")]
    [DataRow("printer.local")]
    [DataRow("metadata.google.internal")]
    [DataRow("1.0.0.127.in-addr.arpa")]
    public void RejectsLocalOnlyHostNames(string host) =>
        AssertOnly("forbidden_allowed_host", Valid with { AllowedHosts = ["records.example.gov", host] });

    [TestMethod]
    public void RejectsEmptyOversizedAndDuplicateHostLists()
    {
        AssertOnly("no_allowed_hosts", Valid with { AllowedHosts = [] });
        AssertOnly("no_allowed_hosts", Valid with { AllowedHosts = null! });
        var many = Enumerable.Range(0, 33).Select(i => $"h{i}.example.gov").ToArray();
        AssertOnly("too_many_allowed_hosts", Valid with { AllowedHosts = many });
        Assert.IsTrue(SourceFetchPolicyAdmission.Validate(Valid with { AllowedHosts = many[..32] }).Admitted);
        AssertOnly("duplicate_allowed_host", Valid with { AllowedHosts = ["a.example.gov", "a.example.gov"] });
    }

    [TestMethod]
    public void RejectsSchemesOutsideHttpsAndHttp()
    {
        AssertOnly("invalid_allowed_scheme", Valid with { AllowedSchemes = [] });
        AssertOnly("invalid_allowed_scheme", Valid with { AllowedSchemes = null! });
        AssertOnly("invalid_allowed_scheme", Valid with { AllowedSchemes = ["ftp"] });
        AssertOnly("invalid_allowed_scheme", Valid with { AllowedSchemes = ["HTTPS"] });
        AssertOnly("invalid_allowed_scheme", Valid with { AllowedSchemes = ["https", "https"] });
        AssertOnly("invalid_allowed_scheme", Valid with { AllowedSchemes = ["https", "file"] });
    }

    [TestMethod]
    public void RequiresAnExplicitExceptionForPlaintextHttp()
    {
        AssertOnly("http_without_exception", Valid with { AllowedSchemes = ["https", "http"] });
        AssertOnly("http_without_exception", Valid with { AllowedSchemes = ["http"], AllowedPorts = [80] });
        Assert.IsTrue(SourceFetchPolicyAdmission.Validate(
            Valid with { AllowedSchemes = ["http"], AllowedPorts = [80], PlaintextHttpException = true }).Admitted);
    }

    [TestMethod]
    public void RejectsInvalidPortsAndRedirectBounds()
    {
        AssertOnly("invalid_allowed_port", Valid with { AllowedPorts = [] });
        AssertOnly("invalid_allowed_port", Valid with { AllowedPorts = null! });
        AssertOnly("invalid_allowed_port", Valid with { AllowedPorts = [0] });
        AssertOnly("invalid_allowed_port", Valid with { AllowedPorts = [65536] });
        AssertOnly("invalid_allowed_port", Valid with { AllowedPorts = [-443] });
        AssertOnly("invalid_allowed_port", Valid with { AllowedPorts = [443, 443] });
        AssertOnly("invalid_max_redirects", Valid with { MaxRedirects = -1 });
        AssertOnly("invalid_max_redirects", Valid with { MaxRedirects = 11 });
    }

    [TestMethod]
    public void ReportsEveryViolationInAFixedOrder()
    {
        var policy = new SourceFetchPolicy(
            PolicyVersion: "",
            Jurisdiction: "US",
            SourceType: "Court",
            AllowedHosts: ["10.0.0.1", "x.local", "x.local"],
            AllowedSchemes: ["ftp", "http"],
            AllowedPorts: [0],
            MaxRedirects: 99);
        CollectionAssert.AreEqual(
            new[]
            {
                "invalid_policy_version", "invalid_jurisdiction", "invalid_source_type",
                "invalid_allowed_host", "forbidden_allowed_host", "duplicate_allowed_host",
                "invalid_allowed_scheme", "http_without_exception", "invalid_allowed_port",
                "invalid_max_redirects",
            },
            Reasons(policy).ToArray());
    }

    [TestMethod]
    public void TheDestinationGuardAdmitsPlaintextHttpOnlyUnderTheException()
    {
        var expectation = new FetchExpectation("registry-v3", "us-federal", "court_record");
        var hop = new FetchHop("http://records.example.gov/case/1", [IPAddress.Parse("93.184.216.34")]);
        var listedOnly = Valid with { AllowedSchemes = ["https", "http"], AllowedPorts = [443, 80] };

        Assert.AreEqual("scheme_not_allowed",
            FetchDestinationGuard.EvaluateHop(listedOnly, expectation, hop).Reason);
        Assert.AreEqual("scheme_not_allowed",
            FetchDestinationGuard.EvaluateHop(Valid with { PlaintextHttpException = true }, expectation, hop).Reason);

        var excepted = FetchDestinationGuard.EvaluateHop(
            listedOnly with { PlaintextHttpException = true }, expectation, hop);
        Assert.IsTrue(excepted.Admitted);
        Assert.AreEqual("http://records.example.gov/case/1", excepted.FinalUri);

        var https = new FetchHop("https://records.example.gov/case/1", [IPAddress.Parse("93.184.216.34")]);
        Assert.IsTrue(FetchDestinationGuard.EvaluateHop(listedOnly, expectation, https).Admitted);
    }
}
