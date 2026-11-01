namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// The attestation as recorded (VER-P0-03 slice 12). All values are the strings stored, compared
/// exactly: nothing is trimmed, case-folded or otherwise normalised here.
/// </summary>
public sealed record AttestationClaim(
    string? RetrieverIdentity,
    string? PublisherIdentity,
    string? BindingMechanism,
    string? BindingReference,
    string? AuthenticationResult,
    string? ContentSha256,
    string? FinalUri,
    string? Jurisdiction,
    string? SourceType,
    DateTime RetrievedAtUtc,
    DateTime AttestedAtUtc);

/// <summary>
/// The verdict of the trusted registry / signature boundary for exactly these claim fields. A stored
/// attestation is an assertion, not proof: only an explicit <paramref name="Verified"/> result that
/// echoes the claim's publisher, mechanism, reference, URI and digest can bind it.
/// </summary>
public sealed record PublisherBindingVerification(
    string? PublisherIdentity,
    string? BindingMechanism,
    string? BindingReference,
    string? FinalUri,
    string? ContentSha256,
    bool Verified);

/// <summary>What the stored raw retrieval says about itself.</summary>
public sealed record AttestedRetrieval(
    string? ContentSha256,
    string? FinalUri,
    DateTime RetrievedAtUtc);

/// <summary>The registry classification the retrieval must carry, plus a trusted server instant.</summary>
public sealed record AttestationExpectation(
    string? Jurisdiction,
    string? SourceType,
    DateTime NowUtc);

public sealed record AttestationBindingDecision(bool Bound, string? Reason)
{
    public static readonly AttestationBindingDecision Ok = new(true, null);
    public static AttestationBindingDecision Refuse(string reason) => new(false, reason);
}

/// <summary>
/// VER-P0-06 slice 5: pure, deterministic, fail-closed check that an attestation really binds a
/// trusted publisher to the normalized final URI and the exact retrieved bytes. A
/// <c>Bound</c> decision is the only thing that may feed
/// <see cref="RawRetrievalProvenanceState.RetrievalAttested"/>; it never derives or sets an
/// <c>ev1:</c> reference and makes nothing answerable. The first failing rule wins, in this fixed
/// order: <c>invalid_input</c>, <c>missing_identity</c>, <c>invalid_binding_mechanism</c>,
/// <c>missing_binding_reference</c>, <c>not_authenticated</c>, <c>digest_mismatch</c>,
/// <c>uri_mismatch</c>, <c>jurisdiction_mismatch</c>, <c>source_type_mismatch</c>,
/// <c>retrieval_time_mismatch</c>, <c>publisher_binding_unverified</c>, <c>attested_before_retrieval</c>, <c>attested_in_future</c>. No clock or I/O: the instant is an
/// explicit input.
/// </summary>
public static class RetrievalAttestationBinding
{
    private static readonly HashSet<string> Mechanisms = new(StringComparer.Ordinal)
    {
        "source_registry",
        "publisher_signature",
    };

    public static AttestationBindingDecision Evaluate(
        AttestationClaim? claim,
        AttestedRetrieval? retrieval,
        AttestationExpectation? expectation,
        PublisherBindingVerification? verification)
    {
        if (claim is null || retrieval is null || expectation is null || verification is null
            || claim.RetrievedAtUtc.Kind != DateTimeKind.Utc
            || claim.AttestedAtUtc.Kind != DateTimeKind.Utc
            || retrieval.RetrievedAtUtc.Kind != DateTimeKind.Utc
            || expectation.NowUtc.Kind != DateTimeKind.Utc)
            return AttestationBindingDecision.Refuse("invalid_input");
        if (string.IsNullOrWhiteSpace(claim.RetrieverIdentity) || string.IsNullOrWhiteSpace(claim.PublisherIdentity))
            return AttestationBindingDecision.Refuse("missing_identity");
        if (claim.BindingMechanism is null || !Mechanisms.Contains(claim.BindingMechanism))
            return AttestationBindingDecision.Refuse("invalid_binding_mechanism");
        if (string.IsNullOrWhiteSpace(claim.BindingReference))
            return AttestationBindingDecision.Refuse("missing_binding_reference");
        if (!string.Equals(claim.AuthenticationResult, "authenticated", StringComparison.Ordinal))
            return AttestationBindingDecision.Refuse("not_authenticated");
        if (!PresentAndEqual(claim.ContentSha256, retrieval.ContentSha256))
            return AttestationBindingDecision.Refuse("digest_mismatch");
        if (!PresentAndEqual(claim.FinalUri, retrieval.FinalUri))
            return AttestationBindingDecision.Refuse("uri_mismatch");
        if (!PresentAndEqual(claim.Jurisdiction, expectation.Jurisdiction))
            return AttestationBindingDecision.Refuse("jurisdiction_mismatch");
        if (!PresentAndEqual(claim.SourceType, expectation.SourceType))
            return AttestationBindingDecision.Refuse("source_type_mismatch");
        if (claim.RetrievedAtUtc != retrieval.RetrievedAtUtc)
            return AttestationBindingDecision.Refuse("retrieval_time_mismatch");
        if (!verification.Verified
            || !PresentAndEqual(verification.PublisherIdentity, claim.PublisherIdentity)
            || !PresentAndEqual(verification.BindingMechanism, claim.BindingMechanism)
            || !PresentAndEqual(verification.BindingReference, claim.BindingReference)
            || !PresentAndEqual(verification.FinalUri, claim.FinalUri)
            || !PresentAndEqual(verification.ContentSha256, claim.ContentSha256))
            return AttestationBindingDecision.Refuse("publisher_binding_unverified");
        if (claim.AttestedAtUtc < retrieval.RetrievedAtUtc)
            return AttestationBindingDecision.Refuse("attested_before_retrieval");
        if (claim.AttestedAtUtc > expectation.NowUtc)
            return AttestationBindingDecision.Refuse("attested_in_future");
        return AttestationBindingDecision.Ok;
    }

    private static bool PresentAndEqual(string? a, string? b)
        => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.Ordinal);
}
