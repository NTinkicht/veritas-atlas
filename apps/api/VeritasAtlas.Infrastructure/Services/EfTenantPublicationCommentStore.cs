using Microsoft.EntityFrameworkCore;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed comment history for a publication revision (VER-P1-02 slice 2). The revision is
/// resolved inside the caller tenant first, so another tenant's ids answer like unknown ones. Appends take
/// a per-(tenant, revision) advisory lock so the per-revision cap and the idempotency key are race-free,
/// and each new comment writes its audit event (without the body) in the same transaction.
/// </summary>
public sealed class EfTenantPublicationCommentStore : ITenantPublicationCommentStore
{
    public const string AddedAction = "publication.comment_added";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantPublicationCommentStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<CommentAddResult> AddAsync(
        Guid tenantId, Guid revisionId, Guid clientKey, string body, string author, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (clientKey == Guid.Empty) throw new ArgumentException("A client key is required.", nameof(clientKey));
        if (TenantPublicationCommentRules.NormalizeBody(body) != body)
        {
            throw new ArgumentException("A normalized comment body is required.", nameof(body));
        }

        if (TenantPublicationReviewerRules.Normalize(author) != author || author.Length > TenantAuditEventLimits.MaxSubjectLength)
        {
            throw new ArgumentException("A valid author subject is required.", nameof(author));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"publication_comments:{tenantId:D}:{revisionId:D}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        if (!await _db.TenantPublicationRevisions.AsNoTracking().AnyAsync(
                x => x.TenantId == tenantId && x.Id == revisionId, cancellationToken))
        {
            return new CommentAddResult(CommentAddOutcome.NotFound, null);
        }

        var existing = await _db.TenantPublicationComments.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.RevisionId == revisionId && x.ClientKey == clientKey, cancellationToken);
        if (existing is not null)
        {
            return existing.Body == body && existing.AuthorSubject == author
                ? new CommentAddResult(CommentAddOutcome.Replayed, ToModel(existing))
                : new CommentAddResult(CommentAddOutcome.KeyConflict, null);
        }

        var count = await _db.TenantPublicationComments.CountAsync(
            x => x.TenantId == tenantId && x.RevisionId == revisionId, cancellationToken);
        if (count >= TenantPublicationCommentRules.MaxPerRevision)
        {
            return new CommentAddResult(CommentAddOutcome.LimitReached, null);
        }

        // PostgreSQL stores microseconds: keep a returned result identical to a later read.
        var ticks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;
        var now = new DateTime(ticks - ticks % 10, DateTimeKind.Utc);
        var row = new TenantPublicationComment
        {
            TenantId = tenantId, Id = Guid.NewGuid(), RevisionId = revisionId, ClientKey = clientKey,
            AuthorSubject = author, Body = body, CreatedAtUtc = now
        };
        _db.TenantPublicationComments.Add(row);
        var auditId = Guid.NewGuid();
        _db.TenantAuditEvents.Add(new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = auditId,
            EventKey = $"{AddedAction}:{auditId:D}",
            Action = AddedAction,
            EntityType = EfTenantPublicationReviewerAssignmentStore.AuditEntityType,
            EntityId = revisionId.ToString("D"),
            ActorSubject = author,
            OccurredAtUtc = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CommentAddResult(CommentAddOutcome.Created, ToModel(row));
    }

    public async Task<IReadOnlyList<TenantPublicationCommentModel>?> ListAsync(
        Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (!await _db.TenantPublicationRevisions.AsNoTracking().AnyAsync(
                x => x.TenantId == tenantId && x.Id == revisionId, cancellationToken))
        {
            return null;
        }

        var rows = await _db.TenantPublicationComments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RevisionId == revisionId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Take(TenantPublicationCommentRules.MaxPerRevision)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private static TenantPublicationCommentModel ToModel(TenantPublicationComment row) =>
        new(row.Id, row.RevisionId, row.ClientKey, row.AuthorSubject, row.Body,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
