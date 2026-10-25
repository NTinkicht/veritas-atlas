namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-03 slice 1 (#82, owner decision A in #80): tenant-partitioned source
/// registry. Every operation takes the caller's tenant and only ever reads or
/// writes that tenant's rows; another tenant's entry is indistinguishable from
/// a missing one.
/// </summary>
public interface ITenantSourceRegistry
{
    /// <summary>Creates an entry, or returns null when the tenant already has
    /// an entry with the same canonical key. The registration and its
    /// <c>tenant_source.registered</c> audit event (actor
    /// <paramref name="actorSubject"/>) commit together (#99).</summary>
    Task<TenantSourceRegistryEntryModel?> CreateAsync(
        Guid tenantId, string canonicalKey, string actorSubject, CancellationToken cancellationToken);

    Task<TenantSourceRegistryEntryModel?> GetAsync(
        Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantSourceRegistryEntryModel>> ListAsync(
        Guid tenantId, CancellationToken cancellationToken);
}

public sealed record TenantSourceRegistryEntryModel(Guid Id, string CanonicalKey, DateTime CreatedAtUtc);

public static class TenantSourceRegistryLimits
{
    public const int MaxCanonicalKeyLength = 512;
    public const int MaxListSize = 200;
}
