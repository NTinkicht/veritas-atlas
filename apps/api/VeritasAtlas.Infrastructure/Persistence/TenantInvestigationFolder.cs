namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// A subject's private folder for a tenant investigation (VER-P1-01 slice 3), one row per
/// (tenant, investigation, subject). The schema refuses a row whose investigation is not of the same
/// tenant, any change of its key, and any write while the investigation is closed.
/// </summary>
public sealed class TenantInvestigationFolder
{
    public Guid TenantId { get; set; }

    public Guid InvestigationId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}
