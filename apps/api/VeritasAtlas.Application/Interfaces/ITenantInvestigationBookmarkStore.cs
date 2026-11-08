namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-10 slice 5: private per-subject bookmarks on evidence items collected into a tenant
/// investigation. Every read and write is scoped to the caller tenant and subject, and another
/// tenant's investigation or item is indistinguishable from an unknown one. Bookmarking never
/// touches the evidence item or the original source.
/// </summary>
public interface ITenantInvestigationBookmarkStore
{
    /// <summary>Idempotent: bookmarking an already bookmarked item replays the original row.</summary>
    Task<BookmarkPutResult> PutAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, string subject, CancellationToken cancellationToken);

    /// <summary>Removes only the caller's own bookmark; removing an absent bookmark is a no-op.</summary>
    Task<BookmarkRemoveOutcome> RemoveAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, string subject, CancellationToken cancellationToken);

    /// <summary>The caller's own bookmarks; null when the investigation does not exist in the tenant.</summary>
    Task<IReadOnlyList<TenantInvestigationBookmarkModel>?> ListAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken);
}

public sealed record TenantInvestigationBookmarkModel(
    Guid InvestigationId, Guid EvidenceItemId, string Subject, DateTime CreatedAtUtc);

public enum BookmarkPutOutcome
{
    Added,
    Replayed,
    NotFound
}

public sealed record BookmarkPutResult(BookmarkPutOutcome Outcome, TenantInvestigationBookmarkModel? Bookmark);

public enum BookmarkRemoveOutcome
{
    Removed,
    NotBookmarked,
    NotFound
}
