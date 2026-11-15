namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// Append-only close/reopen event of a tenant investigation (VER-P0-10 slice 8). The creation
/// record is never touched: the current state is the event with the highest version, and an
/// investigation without events is open at version 0. The schema refuses UPDATE/DELETE/TRUNCATE
/// and requires versions to be gapless with alternating states.
/// </summary>
public sealed class TenantInvestigationStateEvent
{
    public Guid TenantId { get; set; }

    public Guid InvestigationId { get; set; }

    public int Version { get; set; }

    /// <summary><c>closed</c> or <c>open</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string ActorSubject { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
