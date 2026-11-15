using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed immutable investigation records (VER-P0-03 slice 5,
/// #93). The database enforces the tenant key, title shape and refuses
/// UPDATE/DELETE/TRUNCATE; this service checks the same rules first so callers
/// get a typed outcome rather than a raw error. Every read is filtered to the
/// caller tenant. Each created investigation writes its
/// <c>investigation.created</c> audit event in the same transaction (#97).
/// </summary>
public sealed class EfTenantInvestigationStore : ITenantInvestigationStore
{
    private const string UniqueViolation = "23505";
    public const string CreatedAction = "investigation.created";
    public const string ClosedAction = "investigation.closed";
    public const string ReopenedAction = "investigation.reopened";
    public const string AuditEntityType = "investigation";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<InvestigationCreateResult> CreateAsync(
        Guid tenantId, InvestigationInput input, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        Validate(input);

        var existing = await FindByKeyAsync(tenantId, input.CreationKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, input, await LatestEventAsync(tenantId, existing.Id, cancellationToken));
        }

        var row = new TenantInvestigation
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            CreationKey = input.CreationKey,
            Title = input.Title,
            CreatedBySubject = input.CreatedBySubject,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        // Same SaveChanges, so one transaction: the investigation never
        // exists without its audit event, nor the event without it (#97).
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{CreatedAction}:{row.Id:D}",
            Action = CreatedAction,
            EntityType = AuditEntityType,
            EntityId = row.Id.ToString("D"),
            ActorSubject = row.CreatedBySubject,
            OccurredAtUtc = row.CreatedAtUtc
        };

        _db.TenantInvestigations.Add(row);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;

            // A concurrent writer won the creation key: replay against it.
            var winner = await FindByKeyAsync(tenantId, input.CreationKey, cancellationToken)
                ?? throw new InvalidOperationException("Creation key winner is not visible.", ex);
            return Replay(winner, input, await LatestEventAsync(tenantId, winner.Id, cancellationToken));
        }

        return new InvestigationCreateResult(InvestigationCreateOutcome.Created, ToModel(row));
    }

    public async Task<TenantInvestigationModel?> GetAsync(
        Guid tenantId, Guid investigationId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var row = await _db.TenantInvestigations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken);
        return row is null ? null : ToModel(row, await LatestEventAsync(tenantId, investigationId, cancellationToken));
    }

    public async Task<IReadOnlyList<TenantInvestigationModel>> ListAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var rows = await _db.TenantInvestigations.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var latest = (await _db.TenantInvestigationStateEvents.AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.InvestigationId)
            .ToDictionary(g => g.Key, g => g.MaxBy(x => x.Version)!);
        return rows.Select(r => ToModel(r, latest.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<InvestigationTransitionResult> TransitionAsync(
        Guid tenantId, Guid investigationId, bool close, int expectedVersion, string actorSubject,
        CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        if (expectedVersion < 0
            || string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantInvestigationLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid investigation transition input.", nameof(actorSubject));
        }

        var row = await _db.TenantInvestigations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken);
        if (row is null)
        {
            return new InvestigationTransitionResult(InvestigationTransitionOutcome.NotFound, null);
        }

        var target = close ? TenantInvestigationStates.Closed : TenantInvestigationStates.Open;
        var latest = await LatestEventAsync(tenantId, investigationId, cancellationToken);
        var current = latest?.Version ?? 0;
        var currentState = latest?.State ?? TenantInvestigationStates.Open;

        // The same request retried after it succeeded: it carried the version before the event.
        if (latest is not null && expectedVersion == current - 1 && currentState == target)
        {
            return new InvestigationTransitionResult(
                InvestigationTransitionOutcome.Replayed, ToModel(row, latest));
        }

        if (expectedVersion != current)
        {
            return new InvestigationTransitionResult(InvestigationTransitionOutcome.StaleVersion, null);
        }

        if (currentState == target)
        {
            return new InvestigationTransitionResult(InvestigationTransitionOutcome.NoChange, null);
        }

        var at = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var evt = new TenantInvestigationStateEvent
        {
            TenantId = tenantId,
            InvestigationId = investigationId,
            Version = current + 1,
            State = target,
            ActorSubject = actorSubject,
            CreatedAtUtc = at
        };
        var action = close ? ClosedAction : ReopenedAction;
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{action}:{investigationId:D}:{evt.Version}",
            Action = action,
            EntityType = AuditEntityType,
            EntityId = investigationId.ToString("D"),
            ActorSubject = actorSubject,
            OccurredAtUtc = at
        };

        // One SaveChanges, one transaction: the state event never exists without its audit event.
        _db.TenantInvestigationStateEvents.Add(evt);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: UniqueViolation or "23514" })
        {
            // A concurrent writer took this version. If it applied this very request it is a
            // replay, otherwise the caller's expected version is now stale.
            _db.Entry(evt).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            var winner = await LatestEventAsync(tenantId, investigationId, cancellationToken);
            return winner is not null && winner.Version == expectedVersion + 1 && winner.State == target
                ? new InvestigationTransitionResult(InvestigationTransitionOutcome.Replayed, ToModel(row, winner))
                : new InvestigationTransitionResult(InvestigationTransitionOutcome.StaleVersion, null);
        }

        return new InvestigationTransitionResult(InvestigationTransitionOutcome.Transitioned, ToModel(row, evt));
    }

    private Task<TenantInvestigationStateEvent?> LatestEventAsync(
        Guid tenantId, Guid investigationId, CancellationToken cancellationToken) =>
        _db.TenantInvestigationStateEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<TenantInvestigation?> FindByKeyAsync(
        Guid tenantId, string creationKey, CancellationToken cancellationToken) =>
        _db.TenantInvestigations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CreationKey == creationKey, cancellationToken);

    private static InvestigationCreateResult Replay(
        TenantInvestigation existing, InvestigationInput input, TenantInvestigationStateEvent? latest)
    {
        var same = existing.Title == input.Title && existing.CreatedBySubject == input.CreatedBySubject;
        return same
            ? new InvestigationCreateResult(InvestigationCreateOutcome.Replayed, ToModel(existing, latest))
            : new InvestigationCreateResult(InvestigationCreateOutcome.IdempotencyConflict, null);
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static void Validate(InvestigationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.CreationKey)
            || input.CreationKey.Length > TenantInvestigationLimits.MaxCreationKeyLength)
        {
            throw new ArgumentException("Invalid creation key.", nameof(input));
        }

        if (!IsTitle(input.Title))
        {
            throw new ArgumentException("Invalid investigation title.", nameof(input));
        }

        // Control characters are refused like the audit ledger's actor rule
        // (ck_tenant_audit_events_actor_subject_shape), so wiring the audit
        // event never turns valid-looking input into a raw database error.
        if (string.IsNullOrWhiteSpace(input.CreatedBySubject)
            || input.CreatedBySubject.Length > TenantInvestigationLimits.MaxSubjectLength
            || input.CreatedBySubject.Any(char.IsControl))
        {
            throw new ArgumentException("A creating subject is required.", nameof(input));
        }
    }

    private static bool IsTitle(string? value) => TenantInvestigationLimits.IsValidTitle(value);

    // PostgreSQL stores microseconds: round-trip-stable values keep a created
    // result identical to a later read or replay of the same row.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantInvestigationModel ToModel(TenantInvestigation row, TenantInvestigationStateEvent? latest = null) =>
        new(row.Id, row.CreationKey, row.Title, row.CreatedBySubject,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc),
            latest?.State ?? TenantInvestigationStates.Open, latest?.Version ?? 0);
}
