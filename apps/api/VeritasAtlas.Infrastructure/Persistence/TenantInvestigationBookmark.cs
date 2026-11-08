namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// A subject's private bookmark on an evidence item collected into a tenant investigation
/// (VER-P0-10 slice 5). The schema refuses a row whose evidence item is not of the same tenant and
/// investigation. Removing a bookmark deletes only this row, never the item or its source.
/// </summary>
public sealed class TenantInvestigationBookmark
{
    public Guid TenantId { get; set; }

    public Guid InvestigationId { get; set; }

    public Guid EvidenceItemId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
