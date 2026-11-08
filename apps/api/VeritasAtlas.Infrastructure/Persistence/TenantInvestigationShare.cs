namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// A grant by an investigation's creator letting another subject of the same tenant into the
/// investigation (VER-P0-10 slice 6). Revocation is a soft state so the history stays; the schema
/// refuses deletes, re-pointing and any grant not made by the investigation's creator.
/// </summary>
public sealed class TenantInvestigationShare
{
    public Guid TenantId { get; set; }

    public Guid InvestigationId { get; set; }

    public string GranteeSubject { get; set; } = string.Empty;

    public string GrantedBySubject { get; set; } = string.Empty;

    public DateTime GrantedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedBySubject { get; set; }
}
