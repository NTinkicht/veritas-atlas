namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// One append-only retrieval attestation recorded against a tenant raw
/// retrieval (VER-P0-03 slice 12, #107). The final URI, content digest and
/// retrieval time are those of the linked raw retrieval: no divergent copy is
/// stored. Recording an attestation never verifies it and never sets an
/// evidence reference (VER-P0-06/07). Enum columns hold snake_case values.
/// </summary>
public sealed class TenantRetrievalAttestation
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid RawRetrievalId { get; set; }

    public string AttestationKey { get; set; } = string.Empty;

    public string RetrieverIdentity { get; set; } = string.Empty;

    public string PublisherIdentity { get; set; } = string.Empty;

    public string BindingMechanism { get; set; } = string.Empty;

    public string BindingReference { get; set; } = string.Empty;

    public string AuthenticationResult { get; set; } = string.Empty;

    public string Jurisdiction { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public DateTime AttestedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
