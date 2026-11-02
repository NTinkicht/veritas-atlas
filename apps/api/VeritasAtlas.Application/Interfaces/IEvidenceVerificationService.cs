using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// The trusted registry / signature boundary (VER-P0-07 slice 3, #174). A stored attestation is
/// an assertion, not proof; only an implementation of this seam may say a publisher binding is
/// verified. The only production implementation shipped so far always answers
/// <c>Verified = false</c>, so nothing is marked verified until a real verifier is supplied.
/// </summary>
public interface IPublisherBindingVerifier
{
    Task<PublisherBindingVerification> VerifyAsync(AttestationClaim claim, CancellationToken cancellationToken);
}

/// <summary>Fail-closed production verifier: never verifies anything.</summary>
public sealed class FailClosedPublisherBindingVerifier : IPublisherBindingVerifier
{
    public Task<PublisherBindingVerification> VerifyAsync(AttestationClaim claim, CancellationToken cancellationToken) =>
        Task.FromResult(new PublisherBindingVerification(
            claim?.PublisherIdentity, claim?.BindingMechanism, claim?.BindingReference,
            claim?.FinalUri, claim?.ContentSha256, Verified: false));
}

/// <summary>
/// Trusted classification the retrieval must carry (from the source registry, never from the
/// attestation being checked): normalized jurisdiction code and snake_case source type.
/// </summary>
public sealed record EvidenceClassificationExpectation(string Jurisdiction, string SourceType);

/// <summary>
/// VER-P0-07 slice 3 (#174): the only path that marks a parse run "verified and answerable".
/// A verified row is written only when the parse succeeded, its chunks re-derive from the raw
/// bytes, an authenticated attestation binds the exact retrieval and the trusted verifier
/// confirms the publisher binding; anything else returns a typed refusal and writes nothing.
/// </summary>
public interface IEvidenceVerificationService
{
    Task<EvidenceVerificationResult> VerifyAsync(
        Guid tenantId, Guid parseRunId, EvidenceClassificationExpectation expectation,
        string actorSubject, CancellationToken cancellationToken);

    /// <summary>True only when a verified row exists for this parse run of the caller's tenant.</summary>
    Task<bool> IsVerifiedAsync(Guid tenantId, Guid parseRunId, CancellationToken cancellationToken);
}

public enum EvidenceVerificationOutcome
{
    /// <summary>A new append-only verified row was written.</summary>
    Verified,

    /// <summary>The run was already verified: the original row.</summary>
    Replayed,

    /// <summary>A check failed (see <see cref="EvidenceVerificationResult.RefusalReason"/>); nothing written.</summary>
    Refused,

    /// <summary>The parse run is unknown or belongs to another tenant.</summary>
    RunNotFound
}

public sealed record EvidenceVerificationModel(
    Guid Id, Guid ParseRunId, Guid RawRetrievalId, Guid AttestationId,
    string ParentEvidenceRef, DateTime VerifiedAtUtc);

public sealed record EvidenceVerificationResult(
    EvidenceVerificationOutcome Outcome, EvidenceVerificationModel? Verification, string? RefusalReason);
