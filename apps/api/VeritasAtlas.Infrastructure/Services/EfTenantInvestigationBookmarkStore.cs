using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed per-subject bookmarks (VER-P0-10 slice 5). The item is resolved inside the
/// caller tenant first so another tenant's ids are indistinguishable from unknown ones; the
/// database guard trigger enforces the same rule. Each effective add or removal writes its audit
/// event in the same transaction; a replay or a no-op removal writes none.
/// </summary>
public sealed class EfTenantInvestigationBookmarkStore : ITenantInvestigationBookmarkStore
{
    private const string UniqueViolation = "23505";
    private const string GuardViolation = "23514";
    private const int MaxPutAttempts = 3;
    public const string AddedAction = "investigation.bookmark_added";
    public const string RemovedAction = "investigation.bookmark_removed";
    public const string AuditEntityType = "investigation_bookmark";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationBookmarkStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<BookmarkPutResult> PutAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);

        // A unique-violation winner may be removed again before we can read it: retry a bounded
        // number of times, re-checking the item each time.
        for (var attempt = 0; attempt < MaxPutAttempts; attempt++)
        {
            if (!await ItemExistsAsync(tenantId, investigationId, evidenceItemId, cancellationToken))
            {
                return new BookmarkPutResult(BookmarkPutOutcome.NotFound, null);
            }

            var existing = await FindAsync(tenantId, evidenceItemId, subject, cancellationToken);
            if (existing is not null)
            {
                return new BookmarkPutResult(BookmarkPutOutcome.Replayed, ToModel(existing));
            }

            var row = new TenantInvestigationBookmark
            {
                TenantId = tenantId,
                InvestigationId = investigationId,
                EvidenceItemId = evidenceItemId,
                Subject = subject,
                CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
            };
            var audit = NewAudit(AddedAction, row, row.CreatedAtUtc);
            _db.TenantInvestigationBookmarks.Add(row);
            _db.TenantAuditEvents.Add(audit);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return new BookmarkPutResult(BookmarkPutOutcome.Added, ToModel(row));
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: GuardViolation })
            {
                // The guard trigger refused: the item vanished after the existence check.
                _db.Entry(row).State = EntityState.Detached;
                _db.Entry(audit).State = EntityState.Detached;
                return new BookmarkPutResult(BookmarkPutOutcome.NotFound, null);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
            {
                _db.Entry(row).State = EntityState.Detached;
                _db.Entry(audit).State = EntityState.Detached;
                var winner = await FindAsync(tenantId, evidenceItemId, subject, cancellationToken);
                if (winner is not null)
                {
                    return new BookmarkPutResult(BookmarkPutOutcome.Replayed, ToModel(winner));
                }
                // Winner already removed: loop and insert again (bookmark and audit stay atomic).
            }
        }

        throw new InvalidOperationException("Bookmark could not be stored after repeated concurrent removals.");
    }

    public async Task<BookmarkRemoveOutcome> RemoveAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        if (!await ItemExistsAsync(tenantId, investigationId, evidenceItemId, cancellationToken))
        {
            return BookmarkRemoveOutcome.NotFound;
        }

        var row = await _db.TenantInvestigationBookmarks.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.EvidenceItemId == evidenceItemId && x.Subject == subject,
            cancellationToken);
        if (row is null)
        {
            return BookmarkRemoveOutcome.NotBookmarked;
        }

        _db.TenantInvestigationBookmarks.Remove(row);
        _db.TenantAuditEvents.Add(NewAudit(RemovedAction, row, ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent removal already deleted the row: the end state is the same.
            _db.ChangeTracker.Clear();
            return BookmarkRemoveOutcome.NotBookmarked;
        }

        return BookmarkRemoveOutcome.Removed;
    }

    public async Task<IReadOnlyList<TenantInvestigationBookmarkModel>?> ListAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        if (!await _db.TenantInvestigations.AsNoTracking().AnyAsync(
                x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken))
        {
            return null;
        }

        var rows = await _db.TenantInvestigationBookmarks.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.EvidenceItemId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<bool> ItemExistsAsync(
        Guid tenantId, Guid investigationId, Guid evidenceItemId, CancellationToken cancellationToken) =>
        _db.TenantInvestigationEvidenceItems.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Id == evidenceItemId,
            cancellationToken);

    private Task<TenantInvestigationBookmark?> FindAsync(
        Guid tenantId, Guid evidenceItemId, string subject, CancellationToken cancellationToken) =>
        _db.TenantInvestigationBookmarks.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.EvidenceItemId == evidenceItemId && x.Subject == subject,
            cancellationToken);

    private static TenantAuditEvent NewAudit(string action, TenantInvestigationBookmark row, DateTime at)
    {
        var id = Guid.NewGuid();
        return new TenantAuditEvent
        {
            TenantId = row.TenantId,
            Id = id,
            EventKey = $"{action}:{id:D}",
            Action = action,
            EntityType = AuditEntityType,
            EntityId = row.EvidenceItemId.ToString("D"),
            ActorSubject = row.Subject,
            OccurredAtUtc = at
        };
    }

    private static void Require(Guid tenantId, string subject)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(subject)
            || subject.Length > TenantInvestigationLimits.MaxSubjectLength
            || subject.Length > TenantAuditEventLimits.MaxSubjectLength
            || subject.Any(char.IsControl))
        {
            throw new ArgumentException("A valid subject is required.", nameof(subject));
        }
    }

    // PostgreSQL stores microseconds: keep a created result identical to a later read.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantInvestigationBookmarkModel ToModel(TenantInvestigationBookmark row) =>
        new(row.InvestigationId, row.EvidenceItemId, row.Subject, DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
