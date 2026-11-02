namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// One immutable chunk produced by a successful parser run (VER-P0-07 slice 2, #157).
/// <see cref="ParentEvidenceRef"/> is the <c>ev1:</c> reference the parser derived; the chunk
/// is NOT verified or answerable by existing: verification needs the attestation and
/// classification proof and is a separate step. A chunk can only point at a
/// <c>parsed_unverified</c> run of the same tenant and the same raw retrieval.
/// </summary>
public sealed class TenantEvidenceChunk
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid ParseRunId { get; set; }

    public Guid RawRetrievalId { get; set; }

    public string ParentEvidenceRef { get; set; } = string.Empty;

    public int Ordinal { get; set; }

    public string ElementId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string TextSha256 { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
