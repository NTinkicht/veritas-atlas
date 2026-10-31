namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// One append-only fetch policy revision of a tenant source registry entry
/// (VER-P0-04 slice 5a, #119). Columns mirror
/// <see cref="VeritasAtlas.Domain.Provenance.SourceFetchPolicy"/>. The current
/// policy of a source is its highest <see cref="Revision"/>; revisions and
/// policy versions are unique per source, so a version names exactly one
/// policy and two concurrent writers of the same revision cannot both commit.
/// Admission and change rules (SourceFetchPolicyAdmission /
/// SourceFetchPolicyChange) are applied by the registry service before a row
/// is written; the schema enforces the shapes it can on its own.
/// </summary>
public sealed class TenantSourceFetchPolicy
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public Guid SourceRegistryEntryId { get; set; }

    public int Revision { get; set; }

    public string PolicyVersion { get; set; } = string.Empty;

    public string Jurisdiction { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string[] AllowedHosts { get; set; } = [];

    public string[] AllowedSchemes { get; set; } = [];

    public int[] AllowedPorts { get; set; } = [];

    public int MaxRedirects { get; set; }

    public bool PlaintextHttpException { get; set; }

    public bool WideningApproved { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
