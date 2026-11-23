namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P1-02 slice 1: the single reviewer a publisher assigns to a publication revision. The assignment
/// is routing metadata only; it never grants or removes the lifecycle permissions of any role.
/// </summary>
public interface ITenantPublicationReviewerAssignmentStore
{
    Task<ReviewerAssignResult> AssignAsync(
        Guid tenantId, Guid revisionId, string reviewer, string assignedBy, CancellationToken cancellationToken);

    Task<ReviewerUnassignOutcome> UnassignAsync(
        Guid tenantId, Guid revisionId, string actor, CancellationToken cancellationToken);

    /// <summary>Null revision found means unknown id; a found revision with a null assignment has no reviewer.</summary>
    Task<ReviewerGetResult> GetAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken);
}

public sealed record TenantPublicationReviewerAssignmentModel(
    Guid RevisionId, string Reviewer, string AssignedBy, DateTime AssignedAtUtc);

public enum ReviewerAssignOutcome
{
    Created,
    Changed,
    Replayed,
    NotFound,
    SelfReviewDenied,
    RevisionClosed
}

public sealed record ReviewerAssignResult(ReviewerAssignOutcome Outcome, TenantPublicationReviewerAssignmentModel? Assignment);

public enum ReviewerUnassignOutcome
{
    Cleared,
    NotAssigned,
    NotFound,
    RevisionClosed
}

public sealed record ReviewerGetResult(bool RevisionFound, TenantPublicationReviewerAssignmentModel? Assignment);

public static class TenantPublicationReviewerRules
{
    /// <summary>A reviewer is a subject name: 1..200 printable characters, no surrounding space.</summary>
    public static string? Normalize(string? raw)
    {
        var subject = raw?.Trim();
        return string.IsNullOrEmpty(subject)
               || subject.Length > TenantInvestigationLimits.MaxSubjectLength
               || subject.Any(char.IsControl)
            ? null
            : subject;
    }
}
