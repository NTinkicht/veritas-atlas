using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed tenant source registry (VER-P0-03 slice 1, #82). Every
/// query is filtered by the caller's tenant; duplicate canonical keys within a
/// tenant are rejected by the unique index, also under concurrency.
/// </summary>
public sealed class EfTenantSourceRegistry : ITenantSourceRegistry
{
    private const string UniqueViolation = "23505";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantSourceRegistry(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<TenantSourceRegistryEntryModel?> CreateAsync(
        Guid tenantId, string canonicalKey, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var entry = new TenantSourceRegistryEntry
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            CanonicalKey = canonicalKey,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };
        _db.TenantSourceRegistryEntries.Add(entry);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(entry).State = EntityState.Detached;
            return null;
        }

        return ToModel(entry);
    }

    public async Task<TenantSourceRegistryEntryModel?> GetAsync(
        Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var entry = await _db.TenantSourceRegistryEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        return entry is null ? null : ToModel(entry);
    }

    public async Task<IReadOnlyList<TenantSourceRegistryEntryModel>> ListAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var entries = await _db.TenantSourceRegistryEntries
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.CanonicalKey)
            .ThenBy(x => x.Id)
            .Take(TenantSourceRegistryLimits.MaxListSize)
            .ToListAsync(cancellationToken);
        return entries.Select(ToModel).ToList();
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static TenantSourceRegistryEntryModel ToModel(TenantSourceRegistryEntry entry) =>
        new(entry.Id, entry.CanonicalKey, DateTime.SpecifyKind(entry.CreatedAtUtc, DateTimeKind.Utc));
}
