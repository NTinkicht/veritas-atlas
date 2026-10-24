using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-05 (#85): the .NET normalization and ev1: derivation must agree with the
/// offline reference verifier on every checked-in vector of
/// tests/fixtures/evidence_provenance_v1.json (also run by
/// tests/test_evidence_provenance_vectors.py).
/// </summary>
[TestClass]
public sealed class EvidenceProvenanceVectorTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VeritasAtlas.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory, "repository root (VeritasAtlas.slnx) not found");
        var path = Path.Combine(directory.FullName, "tests", "fixtures", "evidence_provenance_v1.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    });

    private static string? Text(JsonElement envelope, string name) =>
        envelope.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static EvidenceEnvelope ToEnvelope(JsonElement e) => new(
        Text(e, "schema_version"),
        Text(e, "evidence_ref"),
        Text(e, "source_uri"),
        Text(e, "record_id"),
        Text(e, "content_sha256"),
        Text(e, "parser_name"),
        Text(e, "parser_version"),
        Text(e, "retrieved_at"),
        Text(e, "retrieval_attestation"),
        Text(e, "jurisdiction"),
        Text(e, "source_type"),
        Text(e, "classification_proof"));

    [TestMethod]
    public void EveryCheckedInVectorMatchesTheReferenceVerifier()
    {
        var root = Fixture.Value.RootElement;
        Assert.AreEqual("veritas-evidence-provenance-vectors/v1", root.GetProperty("schema").GetString());
        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        Assert.IsTrue(cases.Count >= 25, $"expected the full vector set, found {cases.Count}");

        var pairs = new Dictionary<string, List<string>>();
        var executed = new HashSet<string>();
        foreach (var vector in cases)
        {
            var id = vector.GetProperty("id").GetString()!;
            Assert.IsTrue(executed.Add(id), $"duplicate case id {id}");
            var trust = vector.GetProperty("trust");
            var result = EvidenceProvenance.VerifyEnvelope(
                ToEnvelope(vector.GetProperty("envelope")),
                new EvidenceTrust(
                    trust.GetProperty("retrieval").GetBoolean(),
                    trust.GetProperty("classification").GetBoolean(),
                    trust.GetProperty("digest").GetBoolean()));

            if (vector.GetProperty("expected").GetString() == "accept")
            {
                Assert.IsTrue(result.Accepted, $"{id}: {result.Reason}");
                Assert.IsNull(result.Reason, id);
                Assert.AreEqual(vector.GetProperty("expected_normalized_source_uri").GetString(), result.NormalizedSourceUri, id);
                Assert.AreEqual(vector.GetProperty("expected_evidence_ref").GetString(), result.EvidenceRef, id);
                if (vector.TryGetProperty("pair", out var pair))
                {
                    var key = pair.GetString()!;
                    if (!pairs.TryGetValue(key, out var refs)) pairs[key] = refs = [];
                    refs.Add(result.EvidenceRef!);
                }
            }
            else
            {
                Assert.AreEqual("reject", vector.GetProperty("expected").GetString(), id);
                Assert.IsFalse(result.Accepted, id);
                Assert.AreEqual(vector.GetProperty("reason").GetString(), result.Reason, id);
                Assert.IsNull(result.EvidenceRef, id);
                Assert.IsNull(result.NormalizedSourceUri, id);
            }
        }

        CollectionAssert.AreEquivalent(new[] { "digest-drift", "parser-drift" }, pairs.Keys.ToArray());
        foreach (var (pair, refs) in pairs)
        {
            Assert.AreEqual(2, refs.Count, pair);
            Assert.AreNotEqual(refs[0], refs[1], $"{pair} must change the exact evidence reference");
        }
    }

    [TestMethod]
    public void DerivationHashesTheRfc8785SerializationOfTheIdentityObject()
    {
        // Independent of the fixture: SHA-256 over the exact JCS bytes.
        const string canonical =
            "{\"content_sha256\":\"" + "ab" + "\",\"parser_name\":\"p\\\"q\",\"parser_version\":\"1\\n\\u001f\"," +
            "\"record_id\":\"é\",\"schema_version\":\"1.0\",\"source_uri\":\"https://example.com/\"}";
        var expected = "ev1:" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));

        Assert.AreEqual(
            expected,
            EvidenceProvenance.DeriveEvidenceRef("1.0", "https://example.com/", "é", "ab", "p\"q", "1\n\u001f"));
    }

    [TestMethod]
    public void ParserVersionChangesTheReference()
    {
        var a = EvidenceProvenance.DeriveEvidenceRef("1.0", "https://example.com/", "r", new string('a', 64), "p", "1.0.0");
        var b = EvidenceProvenance.DeriveEvidenceRef("1.0", "https://example.com/", "r", new string('a', 64), "p", "1.0.1");
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    [DataRow("https://[2001:DB8::1]:443/r", "https://[2001:db8::1]/r")]
    [DataRow("https://example.com:8080", "https://example.com:8080/")]
    [DataRow("https://example.com/%2E%2E/a/%2e/b", "https://example.com/a/b")]
    [DataRow("https://example.com/r?q=%2f%41", "https://example.com/r?q=%2FA")]
    public void NormalizesEquivalentSpellings(string raw, string expected) =>
        Assert.AreEqual(expected, EvidenceProvenance.NormalizeSourceUri(raw));

    [TestMethod]
    [DataRow("https://faß.de/r", "unsupported_idna_profile")]
    [DataRow("https://example.com/r?Authorization=x", "secret_bearing_query")]
    [DataRow("https://example.com/r;token=1?a=1&session_token=2", "secret_bearing_query")]
    [DataRow("https://example.com /r", "malformed_source_uri")]
    [DataRow("https://example.com/r\u0085x", "malformed_source_uri")]
    [DataRow("https://example.com/r?q=\u009f", "malformed_source_uri")]
    [DataRow("https://example.com/r\u0080", "malformed_source_uri")]
    [DataRow("https://example.com/r\u00a0", "malformed_source_uri")]
    [DataRow("https://example.com/r?q=a\u2028b", "malformed_source_uri")]
    [DataRow("https://example.com/r\tx", "malformed_source_uri")]
    [DataRow("https://example.com/r\u007f", "malformed_source_uri")]
    [DataRow("https://example.com:99999/r", "malformed_source_uri")]
    [DataRow("https://example.com:44a/r", "malformed_source_uri")]
    [DataRow("https://[not-ipv6]/r", "malformed_source_uri")]
    [DataRow("https:///r", "malformed_source_uri")]
    [DataRow("//example.com/r", "malformed_source_uri")]
    [DataRow("https:example.com/r", "malformed_source_uri")]
    [DataRow("https://@example.com/r", "source_uri_userinfo_forbidden")]
    [DataRow("", "missing_source_uri")]
    public void RejectsUnprovableOrUnsafeUris(string raw, string code)
    {
        var error = Assert.ThrowsExactly<ProvenanceException>(() => EvidenceProvenance.NormalizeSourceUri(raw));
        Assert.AreEqual(code, error.Code);
    }
}
