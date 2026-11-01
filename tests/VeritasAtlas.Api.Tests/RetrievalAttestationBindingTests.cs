using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 5 (#135): attestation-binding vectors. Every rule has a refusing vector,
/// the order is fixed, comparisons are exact, and a bound result never implies an ev1 reference.
/// </summary>
[TestClass]
public sealed class RetrievalAttestationBindingTests
{
    private const string Digest = "0000000000000000000000000000000000000000000000000000000000000001";
    private const string Uri = "https://registry.example.gov/records/42";
    private static readonly DateTime Retrieved = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AttestationClaim Claim() => new(
        "retriever-1", "publisher-1", "source_registry", "registry-v7", "authenticated",
        Digest, Uri, "dz", "register", Retrieved.AddMinutes(1));

    private static AttestedRetrieval Retrieval() => new(Digest, Uri, Retrieved);
    private static AttestationExpectation Expect() => new("dz", "register", Now);

    private static void Refused(AttestationBindingDecision d, string reason)
    {
        Assert.IsFalse(d.Bound);
        Assert.AreEqual(reason, d.Reason);
    }

    [TestMethod]
    public void Fully_matching_attestation_is_bound_including_exact_time_boundaries()
    {
        var ok = RetrievalAttestationBinding.Evaluate(Claim(), Retrieval(), Expect());
        Assert.IsTrue(ok.Bound);
        Assert.IsNull(ok.Reason);
        Assert.IsTrue(RetrievalAttestationBinding.Evaluate(Claim() with { AttestedAtUtc = Retrieved }, Retrieval(), Expect()).Bound);
        Assert.IsTrue(RetrievalAttestationBinding.Evaluate(Claim() with { AttestedAtUtc = Now }, Retrieval(), Expect()).Bound);
        Assert.IsTrue(RetrievalAttestationBinding.Evaluate(Claim() with { BindingMechanism = "publisher_signature" }, Retrieval(), Expect()).Bound);
    }

    [TestMethod]
    public void Null_inputs_and_non_utc_instants_are_invalid_input()
    {
        Refused(RetrievalAttestationBinding.Evaluate(null, Retrieval(), Expect()), "invalid_input");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), null, Expect()), "invalid_input");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval(), null), "invalid_input");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { AttestedAtUtc = DateTime.SpecifyKind(Retrieved.AddMinutes(1), DateTimeKind.Unspecified) }, Retrieval(), Expect()), "invalid_input");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval() with { RetrievedAtUtc = DateTime.SpecifyKind(Retrieved, DateTimeKind.Local) }, Expect()), "invalid_input");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval(), Expect() with { NowUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) }), "invalid_input");
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void Blank_identities_are_refused(string? blank)
    {
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { RetrieverIdentity = blank }, Retrieval(), Expect()), "missing_identity");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { PublisherIdentity = blank }, Retrieval(), Expect()), "missing_identity");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { BindingReference = blank }, Retrieval(), Expect()), "missing_binding_reference");
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Source_Registry")]
    [DataRow("tls_only")]
    [DataRow(" source_registry")]
    public void Unknown_or_denormalised_binding_mechanism_is_refused(string? mechanism)
        => Refused(RetrievalAttestationBinding.Evaluate(Claim() with { BindingMechanism = mechanism }, Retrieval(), Expect()), "invalid_binding_mechanism");

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("unauthenticated")]
    [DataRow("failed")]
    [DataRow("Authenticated")]
    [DataRow("authenticated ")]
    public void Only_exactly_authenticated_passes(string? result)
        => Refused(RetrievalAttestationBinding.Evaluate(Claim() with { AuthenticationResult = result }, Retrieval(), Expect()), "not_authenticated");

    [TestMethod]
    public void Digest_uri_and_classification_must_match_exactly()
    {
        var upper = Digest.Replace("1", "A", StringComparison.Ordinal);
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { ContentSha256 = upper }, Retrieval(), Expect()), "digest_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { ContentSha256 = null }, Retrieval() with { ContentSha256 = null }, Expect()), "digest_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval() with { ContentSha256 = Digest.Replace("1", "2", StringComparison.Ordinal) }, Expect()), "digest_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { FinalUri = Uri + "/" }, Retrieval(), Expect()), "uri_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { FinalUri = null }, Retrieval() with { FinalUri = null }, Expect()), "uri_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { Jurisdiction = "DZ" }, Retrieval(), Expect()), "jurisdiction_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval(), Expect() with { Jurisdiction = "fr" }), "jurisdiction_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval(), Expect() with { Jurisdiction = null }), "jurisdiction_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { SourceType = "statute" }, Retrieval(), Expect()), "source_type_mismatch");
        Refused(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval(), Expect() with { SourceType = null }), "source_type_mismatch");
    }

    [TestMethod]
    public void Attestation_time_must_lie_between_retrieval_and_now_by_one_tick()
    {
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { AttestedAtUtc = Retrieved.AddTicks(-1) }, Retrieval(), Expect()), "attested_before_retrieval");
        Refused(RetrievalAttestationBinding.Evaluate(Claim() with { AttestedAtUtc = Now.AddTicks(1) }, Retrieval(), Expect()), "attested_in_future");
    }

    [TestMethod]
    public void Precedence_is_fixed_when_several_rules_fail()
    {
        var bad = Claim() with
        {
            RetrieverIdentity = "",
            AuthenticationResult = "failed",
            ContentSha256 = "x",
            AttestedAtUtc = Now.AddDays(1),
        };
        Refused(RetrievalAttestationBinding.Evaluate(bad, Retrieval(), Expect()), "missing_identity");
        bad = bad with { RetrieverIdentity = "r" };
        Refused(RetrievalAttestationBinding.Evaluate(bad, Retrieval(), Expect()), "not_authenticated");
        bad = bad with { AuthenticationResult = "authenticated" };
        Refused(RetrievalAttestationBinding.Evaluate(bad, Retrieval(), Expect()), "digest_mismatch");
        bad = bad with { ContentSha256 = Digest };
        Refused(RetrievalAttestationBinding.Evaluate(bad, Retrieval(), Expect()), "attested_in_future");
    }

    [TestMethod]
    public void A_bound_decision_does_not_make_a_record_eligible_without_the_parser()
    {
        Assert.IsTrue(RetrievalAttestationBinding.Evaluate(Claim(), Retrieval(), Expect()).Bound);
        var state = new RawRetrievalProvenanceState(Digest, Uri, RetrievalAttested: true, ClassificationProven: true,
            ParserName: null, ParserVersion: null, ParserVerified: false, EvidenceRef: null);
        var eligibility = RawRetrievalEligibility.Evaluate(state);
        Assert.IsFalse(eligibility.Eligible);
        Assert.AreEqual("parser_unverified", eligibility.Reason);
    }
}
