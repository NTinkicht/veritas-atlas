namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// The provenance state composed for one raw retrieval, plus the first reason (if any) that
/// kept it from being attested. <see cref="State"/> is always safe to hand to
/// <see cref="RawRetrievalEligibility"/>.
/// </summary>
public sealed record RawRetrievalProvenanceAssessment(RawRetrievalProvenanceState State, string? Reason);

/// <summary>
/// VER-P0-06 slice 6: the single pure composition that turns the retrieval-path checks into a
/// <see cref="RawRetrievalProvenanceState"/>. It recomputes the SHA-256 of the exact bytes
/// (<see cref="RawContentDigest"/>) and requires a <see cref="RetrievalAttestationBinding"/>
/// <c>Bound</c> decision; <c>RetrievalAttested</c> and <c>ClassificationProven</c> are true only
/// then (the binding already compares jurisdiction and source type to the registry expectation).
/// The retrieval path can never set the parser fields or an <c>ev1:</c> reference: they are
/// always <c>null</c>/<c>false</c> here, so <see cref="RawRetrievalEligibility"/> keeps denying
/// the record with <c>parser_unverified</c> until VER-P0-07. The stored digest is carried into
/// the state only when it matches the bytes. Reasons: <c>invalid_input</c>, then the digest
/// reason (<c>invalid_claimed_digest</c>, <c>digest_mismatch</c>), then the binding reason. No
/// I/O, clock or network.
/// </summary>
public static class RawRetrievalProvenanceComposer
{
    public static RawRetrievalProvenanceAssessment Compose(
        byte[]? rawBytes,
        AttestedRetrieval? retrieval,
        AttestationClaim? claim,
        AttestationExpectation? expectation,
        PublisherBindingVerification? verification)
    {
        var unattested = new RawRetrievalProvenanceState(
            null, retrieval?.FinalUri, false, false, null, null, false, null);
        if (rawBytes is null || retrieval is null || claim is null || expectation is null || verification is null)
            return new RawRetrievalProvenanceAssessment(unattested, "invalid_input");

        var digest = RawContentDigest.Verify(rawBytes, retrieval.ContentSha256);
        if (!digest.Matches)
            return new RawRetrievalProvenanceAssessment(unattested, digest.Reason);

        var verified = unattested with { ContentSha256 = retrieval.ContentSha256 };
        var binding = RetrievalAttestationBinding.Evaluate(claim, retrieval, expectation, verification);
        if (!binding.Bound)
            return new RawRetrievalProvenanceAssessment(verified, binding.Reason);

        return new RawRetrievalProvenanceAssessment(
            verified with { RetrievalAttested = true, ClassificationProven = true }, null);
    }
}
