using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed private per-subject investigation tags (VER-P1-01 slice 2). The investigation is
/// resolved inside the caller tenant first so another tenant's ids are indistinguishable from unknown
/// ones. Adding takes a per-(tenant, investigation, subject) advisory lock so the cap cannot be
/// exceeded concurrently, and each effective add or removal writes its audit event in the same
/// transaction; a replay or a no-op removal writes none.
/// </summary>
public sealed class EfTenantInvestigationTagStore : ITenantInvestigationTagStore
{
    private const string UniqueViolation = "23505";
    private const string GuardViolation = "23514";
    public const string AddedAction = "investigation.tag_added";
    public const string RemovedAction = "investigation.tag_removed";
    public const string AuditEntityType = "investigation_tag";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationTagStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<TagAddResult> AddAsync(
        Guid tenantId, Guid investigationId, string tag, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject, tag);
        if (!await InvestigationExistsAsync(tenantId, investigationId, cancellationToken))
        {
            return new TagAddResult(TagAddOutcome.NotFound, null);
        }

        if (await InvestigationStateQueries.IsClosedAsync(_db, tenantId, investigationId, cancellationToken))
        {
            return new TagAddResult(TagAddOutcome.InvestigationClosed, null);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"investigation_tag:{tenantId:D}:{investigationId:D}:{subject}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        var existing = await _db.TenantInvestigationTags.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject && x.Tag == tag,
            cancellationToken);
        if (existing is not null)
        {
            return new TagAddResult(TagAddOutcome.Replayed, ToModel(existing));
        }

        if (await _db.TenantInvestigationTags.AsNoTracking().CountAsync(
                x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject,
                cancellationToken)
            >= TenantInvestigationTagLimits.MaxPerSubjectPerInvestigation)
        {
            return new TagAddResult(TagAddOutcome.LimitReached, null);
        }

        var row = new TenantInvestigationTag
        {
            TenantId = tenantId,
            InvestigationId = investigationId,
            Subject = subject,
            Tag = tag,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };
        _db.TenantInvestigationTags.Add(row);
        _db.TenantAuditEvents.Add(NewAudit(AddedAction, row, row.CreatedAtUtc));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (InvestigationStateQueries.IsClosedViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return new TagAddResult(TagAddOutcome.InvestigationClosed, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: GuardViolation })
        {
            _db.ChangeTracker.Clear();
            return new TagAddResult(TagAddOutcome.NotFound, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Unreachable while the advisory lock is held; fail safe as a replay of the winner.
            _db.ChangeTracker.Clear();
            return new TagAddResult(TagAddOutcome.Replayed, ToModel(row));
        }

        return new TagAddResult(TagAddOutcome.Added, ToModel(row));
    }

    public async Task<TagRemoveOutcome> RemoveAsync(
        Guid tenantId, Guid investigationId, string tag, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject, tag);
        if (!await InvestigationExistsAsync(tenantId, investigationId, cancellationToken))
        {
            return TagRemoveOutcome.NotFound;
        }

        if (await InvestigationStateQueries.IsClosedAsync(_db, tenantId, investigationId, cancellationToken))
        {
            return TagRemoveOutcome.InvestigationClosed;
        }

        var row = await _db.TenantInvestigationTags.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject && x.Tag == tag,
            cancellationToken);
        if (row is null)
        {
            return TagRemoveOutcome.NotTagged;
        }

        _db.TenantInvestigationTags.Remove(row);
        _db.TenantAuditEvents.Add(NewAudit(RemovedAction, row, ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (InvestigationStateQueries.IsClosedViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return TagRemoveOutcome.InvestigationClosed;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent removal already deleted the row: the end state is the same.
            _db.ChangeTracker.Clear();
            return TagRemoveOutcome.NotTagged;
        }

        return TagRemoveOutcome.Removed;
    }

    public async Task<IReadOnlyList<TenantInvestigationTagModel>?> ListAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject, null);
        if (!await InvestigationExistsAsync(tenantId, investigationId, cancellationToken))
        {
            return null;
        }

        var rows = await _db.TenantInvestigationTags.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject)
            .OrderBy(x => x.Tag)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<bool> InvestigationExistsAsync(Guid tenantId, Guid investigationId, CancellationToken cancellationToken) =>
        _db.TenantInvestigations.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken);

    private static TenantAuditEvent NewAudit(string action, TenantInvestigationTag row, DateTime at)
    {
        var id = Guid.NewGuid();
        return new TenantAuditEvent
        {
            TenantId = row.TenantId,
            Id = id,
            EventKey = $"{action}:{id:D}",
            Action = action,
            EntityType = AuditEntityType,
            EntityId = row.InvestigationId.ToString("D"),
            ActorSubject = row.Subject,
            OccurredAtUtc = at
        };
    }

    private static void Require(Guid tenantId, string subject, string? tag)
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

        if (tag is not null && !TenantInvestigationTagRules.IsValid(tag))
        {
            throw new ArgumentException("A normalized tag is required.", nameof(tag));
        }
    }

    // PostgreSQL stores microseconds: keep a created result identical to a later read.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantInvestigationTagModel ToModel(TenantInvestigationTag row) =>
        new(row.InvestigationId, row.Tag, row.Subject, DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
