namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// Append-only proof that one parse run was verified and answerable (VER-P0-07 slice 3, #174).
/// One row per parse run; the database refuses a row unless the run is a parsed_unverified run
/// of the same tenant and retrieval and the attestation belongs to that retrieval and is
/// authenticated. Rows can never be updated or deleted.
/// </summary>
public sealed class TenantEvidenceVerification
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid ParseRunId { get; set; }

    public Guid RawRetrievalId { get; set; }

    public Guid AttestationId { get; set; }

    public string ParentEvidenceRef { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
