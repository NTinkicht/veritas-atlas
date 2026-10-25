using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed append-only audit event ledger (VER-P0-03 slice 6, #95).
/// The database enforces the tenant key, identifier shapes and refuses
/// UPDATE/DELETE/TRUNCATE; this service checks the same rules first so callers
/// get a typed outcome rather than a raw error. Every read is filtered to the
/// caller tenant.
/// </summary>
public sealed partial class EfTenantAuditEventStore : ITenantAuditEventStore
{
    private const string UniqueViolation = "23505";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantAuditEventStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<AuditEventAppendResult> AppendAsync(
        Guid tenantId, AuditEventInput input, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        Validate(input);

        var existing = await FindByKeyAsync(tenantId, input.EventKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, input);
        }

        var row = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = input.EventKey,
            Action = input.Action,
            EntityType = input.EntityType,
            EntityId = input.EntityId,
            ActorSubject = input.ActorSubject,
            OccurredAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        _db.TenantAuditEvents.Add(row);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(row).State = EntityState.Detached;

            // A concurrent writer won the event key: replay against it.
            var winner = await FindByKeyAsync(tenantId, input.EventKey, cancellationToken)
                ?? throw new InvalidOperationException("Event key winner is not visible.", ex);
            return Replay(winner, input);
        }

        return new AuditEventAppendResult(AuditEventAppendOutcome.Appended, ToModel(row));
    }

    public async Task<IReadOnlyList<TenantAuditEventModel>> ListAsync(
        Guid tenantId, string? entityType, string? entityId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        if (entityId is not null && entityType is null)
        {
            throw new ArgumentException("An entity id filter requires an entity type.", nameof(entityId));
        }

        var query = _db.TenantAuditEvents.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (entityType is not null)
        {
            query = query.Where(x => x.EntityType == entityType);
        }

        if (entityId is not null)
        {
            query = query.Where(x => x.EntityId == entityId);
        }

        var rows = await query
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<TenantAuditEvent?> FindByKeyAsync(
        Guid tenantId, string eventKey, CancellationToken cancellationToken) =>
        _db.TenantAuditEvents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EventKey == eventKey, cancellationToken);

    private static AuditEventAppendResult Replay(TenantAuditEvent existing, AuditEventInput input)
    {
        var same = existing.Action == input.Action
            && existing.EntityType == input.EntityType
            && existing.EntityId == input.EntityId
            && existing.ActorSubject == input.ActorSubject;
        return same
            ? new AuditEventAppendResult(AuditEventAppendOutcome.Replayed, ToModel(existing))
            : new AuditEventAppendResult(AuditEventAppendOutcome.IdempotencyConflict, null);
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static void Validate(AuditEventInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.EventKey)
            || input.EventKey.Length > TenantAuditEventLimits.MaxEventKeyLength)
        {
            throw new ArgumentException("Invalid event key.", nameof(input));
        }

        if (!IsDottedIdentifier(input.Action, TenantAuditEventLimits.MaxActionLength))
        {
            throw new ArgumentException("Invalid audit action.", nameof(input));
        }

        if (!IsDottedIdentifier(input.EntityType, TenantAuditEventLimits.MaxEntityTypeLength))
        {
            throw new ArgumentException("Invalid audit entity type.", nameof(input));
        }

        if (!IsIdentifierText(input.EntityId, TenantAuditEventLimits.MaxEntityIdLength))
        {
            throw new ArgumentException("Invalid audit entity id.", nameof(input));
        }

        if (!IsIdentifierText(input.ActorSubject, TenantAuditEventLimits.MaxSubjectLength))
        {
            throw new ArgumentException("An acting subject is required.", nameof(input));
        }
    }

    // Same shape as ck_tenant_audit_events_action_shape / _entity_type_shape.
    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedIdentifier();

    private static bool IsDottedIdentifier(string? value, int maxLength) =>
        value is not null && value.Length <= maxLength && DottedIdentifier().IsMatch(value);

    // Stricter than the CHECK: char.IsControl covers C0, DEL and C1, and any
    // Unicode whitespace-only value is refused.
    private static bool IsIdentifierText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maxLength
        && !value.Any(char.IsControl);

    // PostgreSQL stores microseconds: round-trip-stable values keep an appended
    // result identical to a later read or replay of the same row.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantAuditEventModel ToModel(TenantAuditEvent row) =>
        new(row.Id, row.EventKey, row.Action, row.EntityType, row.EntityId, row.ActorSubject,
            DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc));
}
