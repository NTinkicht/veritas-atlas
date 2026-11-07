namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// Append-only record that a verified evidence record was collected into a tenant investigation
/// (VER-P0-10 slice 2, #210). The schema refuses UPDATE, DELETE and TRUNCATE, and refuses a row
/// whose investigation or evidence verification is not of the same tenant.
/// </summary>
public sealed class TenantInvestigationEvidenceItem
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid InvestigationId { get; set; }

    public Guid EvidenceVerificationId { get; set; }

    public string? Note { get; set; }

    public string CollectedBySubject { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
