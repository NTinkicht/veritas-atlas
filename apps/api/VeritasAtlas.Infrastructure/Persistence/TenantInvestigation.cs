namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// Immutable tenant-scoped investigation creation record (VER-P0-03 slice 5,
/// #93). The schema refuses UPDATE, DELETE and TRUNCATE.
/// </summary>
public sealed class TenantInvestigation
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string CreationKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string CreatedBySubject { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
