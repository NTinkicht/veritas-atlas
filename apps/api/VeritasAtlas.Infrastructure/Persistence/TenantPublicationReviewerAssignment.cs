namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// The reviewer a publisher assigned to a publication revision (VER-P1-02 slice 1), one row per
/// (tenant, revision). The schema refuses a row whose revision is not of the same tenant, a reviewer
/// equal to the revision author, and any change of its key.
/// </summary>
public sealed class TenantPublicationReviewerAssignment
{
    public Guid TenantId { get; set; }

    public Guid RevisionId { get; set; }

    public string ReviewerSubject { get; set; } = string.Empty;

    public string AssignedBy { get; set; } = string.Empty;

    public DateTime AssignedAtUtc { get; set; }
}
