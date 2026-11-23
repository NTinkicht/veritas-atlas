namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P1-02 slice 2: the append-only editorial comment history of a publication revision. Comments are
/// internal to the tenant, never reach the public reader, and never change what any role may do in the
/// lifecycle.
/// </summary>
public interface ITenantPublicationCommentStore
{
    Task<CommentAddResult> AddAsync(
        Guid tenantId, Guid revisionId, Guid clientKey, string body, string author, CancellationToken cancellationToken);

    /// <summary>Null means the revision is unknown to the tenant; otherwise comments oldest first.</summary>
    Task<IReadOnlyList<TenantPublicationCommentModel>?> ListAsync(
        Guid tenantId, Guid revisionId, CancellationToken cancellationToken);
}

public sealed record TenantPublicationCommentModel(
    Guid Id, Guid RevisionId, Guid ClientKey, string Author, string Body, DateTime CreatedAtUtc);

public enum CommentAddOutcome
{
    Created,
    Replayed,
    KeyConflict,
    LimitReached,
    NotFound
}

public sealed record CommentAddResult(CommentAddOutcome Outcome, TenantPublicationCommentModel? Comment);

public static class TenantPublicationCommentRules
{
    public const int MaxBodyLength = 2000;
    public const int MaxPerRevision = 200;

    /// <summary>1..2000 characters after trimming; line breaks and tabs are allowed, other control characters are not.</summary>
    public static string? NormalizeBody(string? raw)
    {
        var body = raw?.Trim();
        return string.IsNullOrEmpty(body)
               || body.Length > MaxBodyLength
               || body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            ? null
            : body;
    }
}
