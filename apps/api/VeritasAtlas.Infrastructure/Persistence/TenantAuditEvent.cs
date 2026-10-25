namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// Append-only tenant-scoped audit event (VER-P0-03 slice 6, #95). The schema
/// refuses UPDATE, DELETE and TRUNCATE.
/// </summary>
public sealed class TenantAuditEvent
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string EventKey { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string ActorSubject { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}
