namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// The current retrieval checkpoint of one tenant source queue (VER-P0-06 follow-up, #167). The
/// only mutable ledger row: the database allows nothing but a one-step advance of
/// <see cref="Sequence"/> and refuses deletion. It points at the stored raw retrieval of the same
/// tenant and source that completed the sequence.
/// </summary>
public sealed class TenantRetrievalCheckpoint
{
    public Guid TenantId { get; set; }

    public Guid SourceEntryId { get; set; }

    public long Sequence { get; set; }

    public Guid RawRetrievalId { get; set; }

    public string Key { get; set; } = string.Empty;

    public DateTime CompletedAtUtc { get; set; }
}
