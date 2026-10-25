using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed tenant source registry (VER-P0-03 slice 1, #82). Every
/// query is filtered by the caller's tenant; duplicate canonical keys within a
/// tenant are rejected by the unique index, also under concurrency. Each
/// registration writes its <c>tenant_source.registered</c> audit event in the
/// same transaction (#99).
/// </summary>
public sealed class EfTenantSourceRegistry : ITenantSourceRegistry
{
    private const string UniqueViolation = "23505";
    public const string RegisteredAction = "tenant_source.registered";
    public const string AuditEntityType = "tenant_source";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantSourceRegistry(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<TenantSourceRegistryEntryModel?> CreateAsync(
        Guid tenantId, string canonicalKey, string actorSubject, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        // Same rule as the audit ledger's actor check
        // (ck_tenant_audit_events_actor_subject_shape): a typed refusal before
        // the database, never a raw DbUpdateException.
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid actor subject.", nameof(actorSubject));
        }

        var entry = new TenantSourceRegistryEntry
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            CanonicalKey = canonicalKey,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        // Same SaveChanges, so one transaction: the entry never exists without
        // its audit event, nor the event without it (#99).
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{RegisteredAction}:{entry.Id:D}",
            Action = RegisteredAction,
            EntityType = AuditEntityType,
            EntityId = entry.Id.ToString("D"),
            ActorSubject = actorSubject,
            OccurredAtUtc = entry.CreatedAtUtc
        };

        _db.TenantSourceRegistryEntries.Add(entry);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(entry).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
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

    // PostgreSQL stores microseconds: the entry and its audit event carry the
    // same round-trip-stable instant.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantSourceRegistryEntryModel ToModel(TenantSourceRegistryEntry entry) =>
        new(entry.Id, entry.CanonicalKey, DateTime.SpecifyKind(entry.CreatedAtUtc, DateTimeKind.Utc));
}
