using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed reviewer assignment for a publication revision (VER-P1-02 slice 1). The revision is
/// resolved inside the caller tenant first, so another tenant's ids answer like unknown ones. Writes take a
/// per-(tenant, revision) advisory lock and are refused for the author and once the revision is Approved,
/// Published or Withdrawn. Each effective change writes its audit event in the same transaction.
/// </summary>
public sealed class EfTenantPublicationReviewerAssignmentStore : ITenantPublicationReviewerAssignmentStore
{
    private const string GuardViolation = "23514";
    public const string AssignedAction = "publication.reviewer_assigned";
    public const string UnassignedAction = "publication.reviewer_unassigned";
    public const string AuditEntityType = "publication_revision";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantPublicationReviewerAssignmentStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<ReviewerAssignResult> AssignAsync(
        Guid tenantId, Guid revisionId, string reviewer, string assignedBy, CancellationToken cancellationToken)
    {
        Require(tenantId, assignedBy);
        if (TenantPublicationReviewerRules.Normalize(reviewer) != reviewer)
        {
            throw new ArgumentException("A normalized reviewer subject is required.", nameof(reviewer));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(tenantId, revisionId, cancellationToken);
        var revision = await FindRevisionForUpdateAsync(tenantId, revisionId, cancellationToken);
        if (revision is null) return new ReviewerAssignResult(ReviewerAssignOutcome.NotFound, null);
        if (IsClosed(revision)) return new ReviewerAssignResult(ReviewerAssignOutcome.RevisionClosed, null);
        if (string.Equals(revision.AuthorId, reviewer, StringComparison.Ordinal))
        {
            return new ReviewerAssignResult(ReviewerAssignOutcome.SelfReviewDenied, null);
        }

        var now = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var row = await _db.TenantPublicationReviewerAssignments.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.RevisionId == revisionId, cancellationToken);
        ReviewerAssignOutcome outcome;
        if (row is null)
        {
            row = new TenantPublicationReviewerAssignment
            {
                TenantId = tenantId, RevisionId = revisionId, ReviewerSubject = reviewer, AssignedBy = assignedBy, AssignedAtUtc = now
            };
            _db.TenantPublicationReviewerAssignments.Add(row);
            outcome = ReviewerAssignOutcome.Created;
        }
        else if (row.ReviewerSubject == reviewer)
        {
            return new ReviewerAssignResult(ReviewerAssignOutcome.Replayed, ToModel(row));
        }
        else
        {
            row.ReviewerSubject = reviewer;
            row.AssignedBy = assignedBy;
            row.AssignedAtUtc = now;
            outcome = ReviewerAssignOutcome.Changed;
        }

        _db.TenantAuditEvents.Add(NewAudit(AssignedAction, tenantId, revisionId, assignedBy, now));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: GuardViolation })
        {
            // Backstop: the schema refused a cross-tenant revision or an author-as-reviewer row.
            _db.ChangeTracker.Clear();
            return new ReviewerAssignResult(ReviewerAssignOutcome.SelfReviewDenied, null);
        }

        return new ReviewerAssignResult(outcome, ToModel(row));
    }

    public async Task<ReviewerUnassignOutcome> UnassignAsync(
        Guid tenantId, Guid revisionId, string actor, CancellationToken cancellationToken)
    {
        Require(tenantId, actor);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(tenantId, revisionId, cancellationToken);
        var revision = await FindRevisionForUpdateAsync(tenantId, revisionId, cancellationToken);
        if (revision is null) return ReviewerUnassignOutcome.NotFound;
        if (IsClosed(revision)) return ReviewerUnassignOutcome.RevisionClosed;
        var row = await _db.TenantPublicationReviewerAssignments.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.RevisionId == revisionId, cancellationToken);
        if (row is null) return ReviewerUnassignOutcome.NotAssigned;

        _db.TenantPublicationReviewerAssignments.Remove(row);
        _db.TenantAuditEvents.Add(NewAudit(
            UnassignedAction, tenantId, revisionId, actor, ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReviewerUnassignOutcome.Cleared;
    }

    public async Task<ReviewerGetResult> GetAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (await FindRevisionAsync(tenantId, revisionId, cancellationToken) is null)
        {
            return new ReviewerGetResult(false, null);
        }

        var row = await _db.TenantPublicationReviewerAssignments.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.RevisionId == revisionId, cancellationToken);
        return new ReviewerGetResult(true, row is null ? null : ToModel(row));
    }

    private Task LockAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
    {
        var lockKey = $"publication_reviewer:{tenantId:D}:{revisionId:D}";
        return _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
    }

    private Task<TenantPublicationRevision?> FindRevisionAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken) =>
        _db.TenantPublicationRevisions.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == revisionId, cancellationToken);

    // Row lock: a concurrent lifecycle transition updates this row, so it waits for this transaction
    // (or this transaction waits for it and then reads the decided state). Without it an assignment
    // could commit after approval and defeat the revision_closed check.
    private Task<TenantPublicationRevision?> FindRevisionForUpdateAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken) =>
        _db.TenantPublicationRevisions.FromSqlInterpolated(
                $"SELECT * FROM tenant_publication_revisions WHERE tenant_id = {tenantId} AND id = {revisionId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private static bool IsClosed(TenantPublicationRevision revision) =>
        revision.State != nameof(LifecycleState.Draft)
        && revision.State != nameof(LifecycleState.Submitted)
        && revision.State != nameof(LifecycleState.ChangesRequested);

    private static TenantAuditEvent NewAudit(string action, Guid tenantId, Guid revisionId, string actor, DateTime at)
    {
        var id = Guid.NewGuid();
        return new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = id,
            EventKey = $"{action}:{id:D}",
            Action = action,
            EntityType = AuditEntityType,
            EntityId = revisionId.ToString("D"),
            ActorSubject = actor,
            OccurredAtUtc = at
        };
    }

    private static void Require(Guid tenantId, string subject)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (TenantPublicationReviewerRules.Normalize(subject) != subject
            || subject.Length > TenantAuditEventLimits.MaxSubjectLength)
        {
            throw new ArgumentException("A valid subject is required.", nameof(subject));
        }
    }

    // PostgreSQL stores microseconds: keep a returned result identical to a later read.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantPublicationReviewerAssignmentModel ToModel(TenantPublicationReviewerAssignment row) =>
        new(row.RevisionId, row.ReviewerSubject, row.AssignedBy, DateTime.SpecifyKind(row.AssignedAtUtc, DateTimeKind.Utc));
}
