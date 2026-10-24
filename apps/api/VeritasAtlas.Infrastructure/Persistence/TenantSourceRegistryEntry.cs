namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// A tenant-partitioned source registry entry (VER-P0-03 slice 1, #82).
/// <see cref="TenantId"/> is part of the primary key and of the canonical-key
/// uniqueness, so no row is addressable without its tenant.
/// </summary>
public sealed class TenantSourceRegistryEntry
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string CanonicalKey { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
