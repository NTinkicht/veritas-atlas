namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-03 slice 12 (#107): append-only, tenant-scoped record of the
/// retrieval attestation fields that <c>docs/EVIDENCE_PROVENANCE_CONTRACT.md</c>
/// requires (retriever and publisher identity, publisher-binding mechanism and
/// reference, source-authentication result, jurisdiction, source type). The
/// normalized final URI, <c>content_sha256</c> and <c>retrieved_at</c> are those
/// of the linked raw retrieval. This only <em>records</em> an attestation: it
/// never verifies one, never sets an evidence reference and never mints
/// <c>ev1:</c> (VER-P0-06/07). Rows can never be updated or deleted.
/// </summary>
public interface ITenantRetrievalAttestationLedger
{
    /// <summary>
    /// A newly recorded attestation writes its
    /// <c>tenant_retrieval_attestation.recorded</c> audit event, attributed to
    /// <paramref name="actorSubject"/>, in the same transaction. Replays and
    /// refusals write no event.
    /// </summary>
    Task<RetrievalAttestationRecordResult> RecordAsync(
        Guid tenantId, RetrievalAttestationInput input, string actorSubject, CancellationToken cancellationToken);

    /// <summary>One attestation of the caller tenant, or <c>null</c> (unknown or another tenant's).</summary>
    Task<TenantRetrievalAttestationModel?> GetAsync(
        Guid tenantId, Guid attestationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantRetrievalAttestationModel>> ListForRetrievalAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken);
}

public enum PublisherBindingMechanism
{
    SourceRegistry,
    PublisherSignature
}

public enum SourceAuthenticationResult
{
    Authenticated,
    Unauthenticated,
    Failed
}

/// <summary>The contract's controlled <c>source_type</c> vocabulary.</summary>
public enum AttestedSourceType
{
    CourtRecord,
    Statute,
    Regulation,
    Register,
    Filing,
    OtherPublicRecord
}

/// <summary>
/// <see cref="AttestationKey"/>, <see cref="RetrieverIdentity"/>, <see cref="PublisherIdentity"/> and
/// <see cref="BindingReference"/> are stored exactly as given: not blank, no
/// surrounding whitespace, no control characters. <see cref="Jurisdiction"/> is
/// a normalized lowercase code: <c>[a-z0-9]</c> followed by <c>[a-z0-9._-]</c>.
/// </summary>
public sealed record RetrievalAttestationInput(
    Guid RawRetrievalId,
    string AttestationKey,
    string RetrieverIdentity,
    string PublisherIdentity,
    PublisherBindingMechanism BindingMechanism,
    string BindingReference,
    SourceAuthenticationResult AuthenticationResult,
    string Jurisdiction,
    AttestedSourceType SourceType,
    DateTime AttestedAtUtc);

public sealed record TenantRetrievalAttestationModel(
    Guid Id,
    Guid RawRetrievalId,
    string AttestationKey,
    string RetrieverIdentity,
    string PublisherIdentity,
    PublisherBindingMechanism BindingMechanism,
    string BindingReference,
    SourceAuthenticationResult AuthenticationResult,
    string Jurisdiction,
    AttestedSourceType SourceType,
    DateTime AttestedAtUtc,
    DateTime CreatedAtUtc);

public enum RetrievalAttestationRecordOutcome
{
    /// <summary>A new append-only row was written.</summary>
    Recorded,

    /// <summary>The same attestation key with the same content: the original row.</summary>
    Replayed,

    /// <summary>The attestation key is already bound to a different attestation.</summary>
    IdempotencyConflict,

    /// <summary>The raw retrieval is unknown or belongs to another tenant.</summary>
    RetrievalNotFound
}

public sealed record RetrievalAttestationRecordResult(
    RetrievalAttestationRecordOutcome Outcome, TenantRetrievalAttestationModel? Attestation);
