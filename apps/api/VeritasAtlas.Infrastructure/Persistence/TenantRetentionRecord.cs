namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// Append-only retention record of a raw retrieval (VER-P0-18 slice 3). Versions are gapless from 1, the
/// retain-until never moves earlier, and the schema refuses UPDATE/DELETE/TRUNCATE.
/// </summary>
public sealed class TenantRetentionRecord
{
    public Guid TenantId { get; set; }

    public Guid RawRetrievalId { get; set; }

    public int Version { get; set; }

    public bool LegalHold { get; set; }

    public DateTime? RetainUntilUtc { get; set; }

    public string ActorSubject { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
