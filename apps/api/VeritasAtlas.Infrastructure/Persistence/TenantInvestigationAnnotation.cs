namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// Append-only analyst annotation on an evidence item collected into a tenant investigation
/// (VER-P0-10 slice 4). The schema refuses UPDATE, DELETE and TRUNCATE, and refuses a row whose
/// evidence item is not of the same tenant and investigation. Original sources are never touched.
/// </summary>
public sealed class TenantInvestigationAnnotation
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid InvestigationId { get; set; }

    public Guid EvidenceItemId { get; set; }

    public string Text { get; set; } = string.Empty;

    public string AuthorSubject { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
