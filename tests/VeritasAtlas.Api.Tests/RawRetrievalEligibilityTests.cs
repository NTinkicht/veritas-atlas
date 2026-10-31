using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 2 (#129): a stored raw retrieval is never citable merely because its bytes
/// were hashed and stored; only a fully verified state is eligible, and the first failing rule
/// wins in a fixed order.
/// </summary>
[TestClass]
public sealed class RawRetrievalEligibilityTests
{
    private static readonly string Digest = new('a', 64);
    private static readonly string Ref = "ev1:" + new string('b', 64);

    private static RawRetrievalProvenanceState Verified() => new(
        Digest, "https://registry.example.gov/record/1", true, true, "parser", "1.0.0", true, Ref);

    private static void AssertDeny(RawRetrievalProvenanceState? state, string reason)
    {
        var decision = RawRetrievalEligibility.Evaluate(state);
        Assert.IsFalse(decision.Eligible);
        Assert.AreEqual(reason, decision.Reason);
    }

    [TestMethod]
    public void Only_the_fully_verified_state_is_eligible()
    {
        var decision = RawRetrievalEligibility.Evaluate(Verified());
        Assert.IsTrue(decision.Eligible);
        Assert.IsNull(decision.Reason);
    }

    [TestMethod]
    public void Raw_bytes_with_digest_uri_attestation_and_classification_are_not_eligible()
    {
        var rawOnly = new RawRetrievalProvenanceState(
            Digest, "https://registry.example.gov/record/1", true, true, null, null, false, null);
        AssertDeny(rawOnly, "parser_unverified");
    }

    [TestMethod]
    public void Caller_supplied_evidence_ref_without_parser_verification_is_refused()
    {
        AssertDeny(Verified() with { ParserVerified = false }, "unverified_evidence_ref_present");
        AssertDeny(Verified() with { ParserName = null }, "unverified_evidence_ref_present");
        AssertDeny(Verified() with { ParserVersion = " " }, "unverified_evidence_ref_present");
    }

    [TestMethod]
    public void Parser_identity_is_required_even_when_marked_verified()
    {
        AssertDeny(Verified() with { ParserName = "", EvidenceRef = null }, "parser_unverified");
        AssertDeny(Verified() with { ParserVersion = null, EvidenceRef = null }, "parser_unverified");
        AssertDeny(Verified() with { ParserVerified = false, EvidenceRef = null }, "parser_unverified");
    }

    [TestMethod]
    public void Each_rule_is_enforced_in_isolation()
    {
        AssertDeny(null, "invalid_state");
        AssertDeny(Verified() with { ContentSha256 = null }, "missing_content_digest");
        AssertDeny(Verified() with { ContentSha256 = "  " }, "missing_content_digest");
        AssertDeny(Verified() with { ContentSha256 = new string('A', 64) }, "invalid_sha256");
        AssertDeny(Verified() with { ContentSha256 = new string('a', 63) }, "invalid_sha256");
        AssertDeny(Verified() with { FinalUri = null }, "missing_final_uri");
        AssertDeny(Verified() with { RetrievalAttested = false }, "retrieval_unattested");
        AssertDeny(Verified() with { ClassificationProven = false }, "classification_unproven");
        AssertDeny(Verified() with { EvidenceRef = null }, "missing_evidence_ref");
        AssertDeny(Verified() with { EvidenceRef = "ev1:" + new string('B', 64) }, "invalid_evidence_ref");
        AssertDeny(Verified() with { EvidenceRef = "ev2:" + new string('b', 64) }, "invalid_evidence_ref");
        AssertDeny(Verified() with { EvidenceRef = "ev1:" + new string('b', 63) }, "invalid_evidence_ref");
    }

    [TestMethod]
    public void First_failing_rule_wins_in_the_fixed_order()
    {
        var everythingWrong = new RawRetrievalProvenanceState(null, null, false, false, null, null, false, "junk");
        AssertDeny(everythingWrong, "missing_content_digest");
        AssertDeny(everythingWrong with { ContentSha256 = "xyz" }, "invalid_sha256");
        AssertDeny(everythingWrong with { ContentSha256 = Digest }, "missing_final_uri");
        AssertDeny(everythingWrong with { ContentSha256 = Digest, FinalUri = "https://x.example/" },
            "unverified_evidence_ref_present");
        AssertDeny(everythingWrong with { ContentSha256 = Digest, FinalUri = "https://x.example/", EvidenceRef = null },
            "parser_unverified");
        var parsed = everythingWrong with
        {
            ContentSha256 = Digest, FinalUri = "https://x.example/", ParserName = "p", ParserVersion = "1",
            ParserVerified = true, EvidenceRef = null
        };
        AssertDeny(parsed, "retrieval_unattested");
        AssertDeny(parsed with { RetrievalAttested = true }, "classification_unproven");
        AssertDeny(parsed with { RetrievalAttested = true, ClassificationProven = true }, "missing_evidence_ref");
        AssertDeny(parsed with { RetrievalAttested = true, ClassificationProven = true, EvidenceRef = "junk" },
            "invalid_evidence_ref");
    }

    [TestMethod]
    public void Evaluation_is_deterministic()
    {
        var state = Verified() with { ClassificationProven = false };
        Assert.AreEqual(RawRetrievalEligibility.Evaluate(state), RawRetrievalEligibility.Evaluate(state));
    }
}
