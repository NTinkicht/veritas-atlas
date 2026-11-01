using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 6: the composer attests only when the bytes' digest and the attestation
/// binding both hold, and the retrieval path can never mark a record parser-verified or attach
/// an ev1 reference, so eligibility stays denied until VER-P0-07.
/// </summary>
[TestClass]
public sealed class RawRetrievalProvenanceComposerTests
{
    private const string Uri = "https://registry.example.gov/records/42";
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("raw public record bytes");
    private static readonly string Digest = RawContentDigest.Compute(Bytes);
    private static readonly DateTime Retrieved = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AttestedRetrieval Retrieval() => new(Digest, Uri, Retrieved);
    private static AttestationClaim Claim() => new(
        "retriever-1", "publisher-1", "source_registry", "registry-v7", "authenticated",
        Digest, Uri, "dz", "register", Retrieved, Retrieved.AddMinutes(1));
    private static AttestationExpectation Expect() => new("dz", "register", Now);
    private static PublisherBindingVerification Verified() => new(
        "publisher-1", "source_registry", "registry-v7", Uri, Digest, true);

    private static RawRetrievalProvenanceAssessment Compose(
        byte[]? bytes = null, AttestedRetrieval? r = null, AttestationClaim? c = null,
        AttestationExpectation? e = null, PublisherBindingVerification? v = null)
        => RawRetrievalProvenanceComposer.Compose(bytes ?? Bytes, r ?? Retrieval(), c ?? Claim(), e ?? Expect(), v ?? Verified());

    private static void AssertNeverParserVerified(RawRetrievalProvenanceState s)
    {
        Assert.IsFalse(s.ParserVerified);
        Assert.IsNull(s.ParserName);
        Assert.IsNull(s.ParserVersion);
        Assert.IsNull(s.EvidenceRef);
    }

    [TestMethod]
    public void Matching_digest_and_binding_attest_but_stay_ineligible_until_the_parser_runs()
    {
        var a = Compose();
        Assert.IsNull(a.Reason);
        Assert.AreEqual(Digest, a.State.ContentSha256);
        Assert.AreEqual(Uri, a.State.FinalUri);
        Assert.IsTrue(a.State.RetrievalAttested);
        Assert.IsTrue(a.State.ClassificationProven);
        AssertNeverParserVerified(a.State);
        var eligibility = RawRetrievalEligibility.Evaluate(a.State);
        Assert.IsFalse(eligibility.Eligible);
        Assert.AreEqual("parser_unverified", eligibility.Reason);
    }

    [TestMethod]
    public void Changed_bytes_fail_the_digest_and_attest_nothing()
    {
        var a = Compose(bytes: Encoding.UTF8.GetBytes("raw public record bytez"));
        Assert.AreEqual("digest_mismatch", a.Reason);
        Assert.IsNull(a.State.ContentSha256);
        Assert.IsFalse(a.State.RetrievalAttested);
        Assert.IsFalse(a.State.ClassificationProven);
        AssertNeverParserVerified(a.State);
    }

    [TestMethod]
    public void Malformed_stored_digest_is_refused_without_normalisation()
    {
        var a = Compose(r: Retrieval() with { ContentSha256 = Digest.ToUpperInvariant() });
        Assert.AreEqual("invalid_claimed_digest", a.Reason);
        Assert.IsFalse(a.State.RetrievalAttested);
        Assert.IsNull(a.State.ContentSha256);
    }

    [TestMethod]
    public void Binding_refusals_propagate_with_their_reason_and_never_attest()
    {
        var cases = new (AttestationClaim?, AttestationExpectation?, PublisherBindingVerification?, string)[]
        {
            (Claim() with { AuthenticationResult = "failed" }, null, null, "not_authenticated"),
            (Claim() with { FinalUri = Uri + "/" }, null, null, "uri_mismatch"),
            (null, Expect() with { Jurisdiction = "fr" }, null, "jurisdiction_mismatch"),
            (null, Expect() with { SourceType = "gazette" }, null, "source_type_mismatch"),
            (null, null, Verified() with { Verified = false }, "publisher_binding_unverified"),
            (Claim() with { AttestedAtUtc = Now.AddSeconds(1) }, null, null, "attested_in_future"),
        };
        foreach (var (c, e, v, reason) in cases)
        {
            var a = Compose(c: c, e: e, v: v);
            Assert.AreEqual(reason, a.Reason);
            Assert.IsFalse(a.State.RetrievalAttested, reason);
            Assert.IsFalse(a.State.ClassificationProven, reason);
            Assert.AreEqual(Digest, a.State.ContentSha256, reason);
            AssertNeverParserVerified(a.State);
        }
    }

    [TestMethod]
    public void Null_inputs_fail_closed()
    {
        Assert.AreEqual("invalid_input", RawRetrievalProvenanceComposer.Compose(null, Retrieval(), Claim(), Expect(), Verified()).Reason);
        Assert.AreEqual("invalid_input", RawRetrievalProvenanceComposer.Compose(Bytes, null, Claim(), Expect(), Verified()).Reason);
        Assert.AreEqual("invalid_input", RawRetrievalProvenanceComposer.Compose(Bytes, Retrieval(), null, Expect(), Verified()).Reason);
        Assert.AreEqual("invalid_input", RawRetrievalProvenanceComposer.Compose(Bytes, Retrieval(), Claim(), null, Verified()).Reason);
        var last = RawRetrievalProvenanceComposer.Compose(Bytes, Retrieval(), Claim(), Expect(), null);
        Assert.AreEqual("invalid_input", last.Reason);
        Assert.IsFalse(last.State.RetrievalAttested);
        AssertNeverParserVerified(last.State);
    }

    [TestMethod]
    public void Digest_is_checked_before_the_binding()
    {
        var a = Compose(bytes: Encoding.UTF8.GetBytes("other"), c: Claim() with { AuthenticationResult = "failed" });
        Assert.AreEqual("digest_mismatch", a.Reason);
    }

    [TestMethod]
    public void Empty_bytes_are_a_valid_exact_digest_not_a_special_case()
    {
        var empty = Array.Empty<byte>();
        var d = RawContentDigest.Compute(empty);
        var a = RawRetrievalProvenanceComposer.Compose(
            empty, new AttestedRetrieval(d, Uri, Retrieved), Claim() with { ContentSha256 = d },
            Expect(), Verified() with { ContentSha256 = d });
        Assert.IsNull(a.Reason);
        Assert.IsTrue(a.State.RetrievalAttested);
    }
}
