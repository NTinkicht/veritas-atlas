using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 10 (#145): privacy-safe redirect audit trace and the latching
/// no-credential-forwarding-across-authority-changes rule.
/// </summary>
[TestClass]
public sealed class FetchRedirectTraceTests
{
    private static readonly SourceFetchPolicy Policy = new(
        PolicyVersion: "registry-v3",
        Jurisdiction: "us-federal",
        SourceType: "court_record",
        AllowedHosts: ["records.example.gov", "mirror.example.gov"],
        AllowedSchemes: ["https"],
        AllowedPorts: [443, 8443],
        MaxRedirects: 3);

    private static readonly FetchExpectation Expected = new("registry-v3", "us-federal", "court_record");

    private static FetchHop Hop(string uri) => new(uri, [IPAddress.Parse("93.184.216.34")]);

    [TestMethod]
    public void SameAuthorityChainKeepsCredentialForwardingAndDropsQueryAndFragment()
    {
        var result = FetchRedirectTrace.Build(Policy, Expected,
            [Hop("https://records.example.gov/a?token=SECRET#frag"), Hop("https://RECORDS.example.gov/b")]);
        Assert.IsTrue(result.Admitted);
        Assert.AreEqual("https://records.example.gov/b", result.FinalUri);
        Assert.AreEqual(2, result.Trace.Count);
        Assert.IsTrue(result.Trace[0].HasQuery);
        Assert.IsFalse(result.Trace[1].HasQuery);
        Assert.AreEqual("/a", result.Trace[0].Path);
        Assert.IsTrue(result.Trace.All(e => e.CredentialsMayForward && !e.AuthorityChanged));
        var text = string.Join("|", result.Trace.Select(e => e.ToString()));
        Assert.IsFalse(text.Contains("SECRET") || text.Contains("frag"));
    }

    [TestMethod]
    public void HostChangeLatchesCredentialForwardingOffEvenWhenReturningToOrigin()
    {
        var result = FetchRedirectTrace.Build(Policy, Expected,
            [Hop("https://records.example.gov/a"), Hop("https://mirror.example.gov/b"), Hop("https://records.example.gov/c")]);
        Assert.IsTrue(result.Admitted);
        CollectionAssert.AreEqual(new[] { true, false, false }, result.Trace.Select(e => e.CredentialsMayForward).ToArray());
        CollectionAssert.AreEqual(new[] { false, true, false }, result.Trace.Select(e => e.AuthorityChanged).ToArray());
    }

    [TestMethod]
    public void PortChangeIsAnAuthorityChange()
    {
        var result = FetchRedirectTrace.Build(Policy, Expected,
            [Hop("https://records.example.gov/a"), Hop("https://records.example.gov:8443/b")]);
        Assert.IsTrue(result.Admitted);
        Assert.IsTrue(result.Trace[1].AuthorityChanged);
        Assert.IsFalse(result.Trace[1].CredentialsMayForward);
        Assert.AreEqual(8443, result.Trace[1].Port);
    }

    [TestMethod]
    public void DeniedChainsYieldNoTraceAndKeepTheGuardReason()
    {
        var forbidden = FetchRedirectTrace.Build(Policy, Expected,
            [Hop("https://records.example.gov/a"), new FetchHop("https://mirror.example.gov/b", [IPAddress.Parse("10.0.0.1")])]);
        Assert.IsFalse(forbidden.Admitted);
        Assert.AreEqual("forbidden_address", forbidden.Reason);
        Assert.AreEqual(0, forbidden.Trace.Count);
        Assert.IsNull(forbidden.FinalUri);

        Assert.AreEqual("policy_version_drift",
            FetchRedirectTrace.Build(Policy, Expected with { PolicyVersion = "x" }, [Hop("https://records.example.gov/")]).Reason);
        Assert.AreEqual("invalid_uri", FetchRedirectTrace.Build(Policy, Expected, null).Reason);
        Assert.AreEqual("invalid_uri", FetchRedirectTrace.Build(Policy, Expected, []).Reason);
        Assert.AreEqual("credentials_in_uri",
            FetchRedirectTrace.Build(Policy, Expected, [Hop("https://u:p@records.example.gov/")]).Reason);
        Assert.AreEqual("too_many_redirects",
            FetchRedirectTrace.Build(Policy, Expected, Enumerable.Repeat(Hop("https://records.example.gov/"), 5).ToArray()).Reason);
    }

    [TestMethod]
    public void TrailingDotHostIsADistinctAuthority()
    {
        var result = FetchRedirectTrace.Build(Policy, Expected,
            [Hop("https://records.example.gov/a"), Hop("https://records.example.gov./b")]);
        Assert.IsTrue(result.Admitted);
        Assert.IsTrue(result.Trace[1].AuthorityChanged);
        Assert.IsFalse(result.Trace[1].CredentialsMayForward);
    }
}
