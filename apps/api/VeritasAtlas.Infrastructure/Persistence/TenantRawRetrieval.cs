namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// An immutable raw retrieval of one tenant source (VER-P0-03 slice 2, #87).
/// <see cref="EvidenceReference"/> is the nullable unverified slot reserved for
/// the versioned parser (VER-P0-07); the schema keeps it NULL until then.
/// </summary>
public sealed class TenantRawRetrieval
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid SourceEntryId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string FinalUri { get; set; } = string.Empty;

    public byte[] RawBytes { get; set; } = Array.Empty<byte>();

    public string ContentSha256 { get; set; } = string.Empty;

    public long ByteLength { get; set; }

    public string? EvidenceReference { get; set; }

    public DateTime RetrievedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
