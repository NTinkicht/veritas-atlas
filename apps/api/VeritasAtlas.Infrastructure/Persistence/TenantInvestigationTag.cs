namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// A subject's private tag on a tenant investigation (VER-P1-01 slice 2). The schema refuses a row
/// whose investigation is not of the same tenant and any write while the investigation is closed.
/// Removing a tag deletes only this row.
/// </summary>
public sealed class TenantInvestigationTag
{
    public Guid TenantId { get; set; }

    public Guid InvestigationId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
