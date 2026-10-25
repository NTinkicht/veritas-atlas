namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-03 slice 6 (#95): append-only, tenant-scoped audit event ledger in
/// PostgreSQL. An event can never be updated or deleted. It carries no
/// free-text payload: only identifiers of what happened, to which entity and
/// by which authenticated subject. Reads are filtered to the caller tenant.
/// </summary>
public interface ITenantAuditEventStore
{
    Task<AuditEventAppendResult> AppendAsync(
        Guid tenantId, AuditEventInput input, CancellationToken cancellationToken);

    /// <summary>
    /// The caller tenant's events in (occurred, id) order, optionally narrowed
    /// to one entity type and, with it, one entity id.
    /// </summary>
    Task<IReadOnlyList<TenantAuditEventModel>> ListAsync(
        Guid tenantId, string? entityType, string? entityId, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="Action"/> and <see cref="EntityType"/> are lowercase dotted
/// identifiers (for example <c>investigation.created</c>);
/// <see cref="EntityId"/> and <see cref="ActorSubject"/> are non-blank
/// identifiers without control characters.
/// </summary>
public sealed record AuditEventInput(
    string EventKey,
    string Action,
    string EntityType,
    string EntityId,
    string ActorSubject);

public sealed record TenantAuditEventModel(
    Guid Id,
    string EventKey,
    string Action,
    string EntityType,
    string EntityId,
    string ActorSubject,
    DateTime OccurredAtUtc);

public enum AuditEventAppendOutcome
{
    /// <summary>A new immutable audit event was written.</summary>
    Appended,

    /// <summary>The same event key with the same content: the original row.</summary>
    Replayed,

    /// <summary>The event key is already bound to a different event.</summary>
    IdempotencyConflict
}

public sealed record AuditEventAppendResult(
    AuditEventAppendOutcome Outcome, TenantAuditEventModel? Event);

public static class TenantAuditEventLimits
{
    public const int MaxEventKeyLength = 200;
    public const int MaxActionLength = 100;
    public const int MaxEntityTypeLength = 64;
    public const int MaxEntityIdLength = 200;
    public const int MaxSubjectLength = 200;
}
