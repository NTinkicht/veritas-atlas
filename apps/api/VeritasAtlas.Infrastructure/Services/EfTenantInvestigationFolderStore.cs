using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed private per-subject investigation folder (VER-P1-01 slice 3). The investigation
/// is resolved inside the caller tenant first so another tenant's ids are indistinguishable from
/// unknown ones. Setting takes a per-(tenant, investigation, subject) advisory lock; each effective
/// set, change or clear writes its audit event in the same transaction, a replay or no-op clear none.
/// </summary>
public sealed class EfTenantInvestigationFolderStore : ITenantInvestigationFolderStore
{
    private const string GuardViolation = "23514";
    public const string SetAction = "investigation.folder_set";
    public const string ClearedAction = "investigation.folder_cleared";
    public const string AuditEntityType = "investigation_folder";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantInvestigationFolderStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<FolderSetResult> SetAsync(
        Guid tenantId, Guid investigationId, string name, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        if (TenantInvestigationFolderRules.Normalize(name) != name)
        {
            throw new ArgumentException("A normalized folder name is required.", nameof(name));
        }

        if (!await InvestigationExistsAsync(tenantId, investigationId, cancellationToken))
        {
            return new FolderSetResult(FolderSetOutcome.NotFound, null);
        }

        if (await InvestigationStateQueries.IsClosedAsync(_db, tenantId, investigationId, cancellationToken))
        {
            return new FolderSetResult(FolderSetOutcome.InvestigationClosed, null);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"investigation_folder:{tenantId:D}:{investigationId:D}:{subject}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        var now = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var row = await _db.TenantInvestigationFolders.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject,
            cancellationToken);
        FolderSetOutcome outcome;
        if (row is null)
        {
            row = new TenantInvestigationFolder
            {
                TenantId = tenantId, InvestigationId = investigationId, Subject = subject, Name = name, UpdatedAtUtc = now
            };
            _db.TenantInvestigationFolders.Add(row);
            outcome = FolderSetOutcome.Created;
        }
        else if (row.Name == name)
        {
            return new FolderSetResult(FolderSetOutcome.Replayed, ToModel(row));
        }
        else
        {
            row.Name = name;
            row.UpdatedAtUtc = now;
            outcome = FolderSetOutcome.Changed;
        }

        _db.TenantAuditEvents.Add(NewAudit(SetAction, row, now));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (InvestigationStateQueries.IsClosedViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return new FolderSetResult(FolderSetOutcome.InvestigationClosed, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: GuardViolation })
        {
            _db.ChangeTracker.Clear();
            return new FolderSetResult(FolderSetOutcome.NotFound, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Fail closed: the row vanished underneath the update; report nothing changed.
            _db.ChangeTracker.Clear();
            return new FolderSetResult(FolderSetOutcome.NotFound, null);
        }

        return new FolderSetResult(outcome, ToModel(row));
    }

    public async Task<FolderClearOutcome> ClearAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        if (!await InvestigationExistsAsync(tenantId, investigationId, cancellationToken))
        {
            return FolderClearOutcome.NotFound;
        }

        if (await InvestigationStateQueries.IsClosedAsync(_db, tenantId, investigationId, cancellationToken))
        {
            return FolderClearOutcome.InvestigationClosed;
        }

        // Same per-(tenant, investigation, subject) lock as SetAsync so a concurrent set cannot
        // update a row this clear deletes (which would surface as a concurrency exception).
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"investigation_folder:{tenantId:D}:{investigationId:D}:{subject}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        var row = await _db.TenantInvestigationFolders.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject,
            cancellationToken);
        if (row is null)
        {
            return FolderClearOutcome.NotFiled;
        }

        _db.TenantInvestigationFolders.Remove(row);
        _db.TenantAuditEvents.Add(NewAudit(ClearedAction, row, ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (InvestigationStateQueries.IsClosedViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return FolderClearOutcome.InvestigationClosed;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent clear already deleted the row: the end state is the same.
            _db.ChangeTracker.Clear();
            return FolderClearOutcome.NotFiled;
        }

        return FolderClearOutcome.Cleared;
    }

    public async Task<FolderGetResult> GetAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        if (!await InvestigationExistsAsync(tenantId, investigationId, cancellationToken))
        {
            return new FolderGetResult(false, null);
        }

        var row = await _db.TenantInvestigationFolders.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.InvestigationId == investigationId && x.Subject == subject,
            cancellationToken);
        return new FolderGetResult(true, row is null ? null : ToModel(row));
    }

    private Task<bool> InvestigationExistsAsync(Guid tenantId, Guid investigationId, CancellationToken cancellationToken) =>
        _db.TenantInvestigations.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.Id == investigationId, cancellationToken);

    private static TenantAuditEvent NewAudit(string action, TenantInvestigationFolder row, DateTime at)
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

    // PostgreSQL stores microseconds: keep a returned result identical to a later read.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantInvestigationFolderModel ToModel(TenantInvestigationFolder row) =>
        new(row.InvestigationId, row.Name, row.Subject, DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc));
}
