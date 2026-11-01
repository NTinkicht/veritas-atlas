using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-01 (#155): pins the first permitted source policy entry
/// (tests/fixtures/first_source_policy_v1.json, documented in
/// docs/architecture/FIRST_SOURCE_POLICY.md). The entry must pass connector admission and an
/// exact pin: any added host, downgraded scheme, missing attribution or changed
/// jurisdiction fails closed. The fixture is SYNTHETIC; no network access is used.
/// </summary>
[TestClass]
public sealed class FirstSourcePolicyTests
{
    private static readonly string[] PinnedHosts = ["www.legislation.gov.uk", "legislation.gov.uk"];

    private static JsonObject Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VeritasAtlas.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory, "repository root (VeritasAtlas.slnx) not found");
        var path = Path.Combine(directory.FullName, "tests", "fixtures", "first_source_policy_v1.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static string[] Strings(JsonObject entry, string name) =>
        entry[name]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    private static SourceFetchPolicy ToPolicy(JsonObject e) => new(
        PolicyVersion: e["policy_version"]!.GetValue<string>(),
        Jurisdiction: e["jurisdiction"]!.GetValue<string>(),
        SourceType: e["source_type"]!.GetValue<string>(),
        AllowedHosts: Strings(e, "allowed_hosts"),
        AllowedSchemes: Strings(e, "allowed_schemes"),
        AllowedPorts: e["allowed_ports"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray(),
        MaxRedirects: e["max_redirects"]!.GetValue<int>(),
        PlaintextHttpException: e["plaintext_http_exception"]!.GetValue<bool>());

    /// <summary>Admission plus the exact pin; returns the refusal reasons (empty = accepted).</summary>
    private static List<string> Gate(JsonObject e)
    {
        var reasons = new List<string>(SourceFetchPolicyAdmission.Validate(ToPolicy(e)).Reasons);
        if (!Strings(e, "allowed_hosts").SequenceEqual(PinnedHosts, StringComparer.Ordinal))
            reasons.Add("host_list_not_pinned");
        if (!Strings(e, "allowed_schemes").SequenceEqual(["https"], StringComparer.Ordinal))
            reasons.Add("scheme_not_pinned");
        if (e["jurisdiction"]!.GetValue<string>() != "uk") reasons.Add("jurisdiction_not_pinned");
        var licence = e["licence"]?.AsObject();
        if (licence is null || licence["attribution_required"]?.GetValue<bool>() != true
            || string.IsNullOrWhiteSpace(licence["attribution_text"]?.GetValue<string>()))
            reasons.Add("attribution_missing");
        return reasons;
    }

    private static JsonObject Mutate(Action<JsonObject> change)
    {
        var entry = Load();
        change(entry);
        return entry;
    }

    [TestMethod]
    public void TheCommittedEntryIsAcceptedAndMarkedSynthetic()
    {
        var entry = Load();
        Assert.AreEqual(0, Gate(entry).Count, string.Join(",", Gate(entry)));
        StringAssert.StartsWith(entry["synthetic_marker"]!.GetValue<string>(), "SYNTHETIC");
        Assert.AreEqual("UNVERIFIED", entry["licence"]!["verification"]!.GetValue<string>());
        Assert.AreEqual("UNVERIFIED", entry["freshness"]!["verification"]!.GetValue<string>());
    }

    [TestMethod]
    public void AnAddedHostIsRefused()
    {
        var entry = Mutate(e => e["allowed_hosts"]!.AsArray().Add("extra.example.org"));
        CollectionAssert.Contains(Gate(entry), "host_list_not_pinned");
    }

    [TestMethod]
    public void WildcardAndIpLiteralHostsAreRefusedByAdmission()
    {
        foreach (var host in new[] { "*.legislation.gov.uk", "203.0.113.7" })
        {
            var entry = Mutate(e => e["allowed_hosts"]!.AsArray().Add(host));
            CollectionAssert.Contains(Gate(entry), "invalid_allowed_host", host);
        }
    }

    [TestMethod]
    public void ASchemeDowngradeIsRefused()
    {
        var entry = Mutate(e => e["allowed_schemes"] = new JsonArray("https", "http"));
        var reasons = Gate(entry);
        CollectionAssert.Contains(reasons, "http_without_exception");
        CollectionAssert.Contains(reasons, "scheme_not_pinned");
        var httpOnly = Mutate(e => e["allowed_schemes"] = new JsonArray("http"));
        CollectionAssert.Contains(Gate(httpOnly), "scheme_not_pinned");
    }

    [TestMethod]
    public void MissingOrEmptyAttributionIsRefused()
    {
        var removed = Mutate(e => e.Remove("licence"));
        CollectionAssert.Contains(Gate(removed), "attribution_missing");
        var blank = Mutate(e => e["licence"]!["attribution_text"] = " ");
        CollectionAssert.Contains(Gate(blank), "attribution_missing");
        var notRequired = Mutate(e => e["licence"]!["attribution_required"] = false);
        CollectionAssert.Contains(Gate(notRequired), "attribution_missing");
    }

    [TestMethod]
    public void AWrongJurisdictionIsRefused()
    {
        var entry = Mutate(e => e["jurisdiction"] = "us-federal");
        CollectionAssert.Contains(Gate(entry), "jurisdiction_not_pinned");
    }

    [TestMethod]
    public void FreshnessParametersFollowTheContract()
    {
        var f = Load()["freshness"]!.AsObject();
        Assert.IsTrue(f["freshness_required_for_current_claims"]!.GetValue<bool>());
        Assert.IsTrue(f["max_retrieval_age_seconds"]!.GetValue<int>() >= 0);
        Assert.IsTrue(f["revision_signal_required"]!.GetValue<bool>());
        CollectionAssert.Contains(
            new[] { "etag", "source-version-id", "authoritative-index", "signed-manifest" },
            f["revision_signal"]!.GetValue<string>());
        Assert.IsFalse(string.IsNullOrWhiteSpace(f["replacement_authority"]!.GetValue<string>()));
    }
}
