using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed private saved searches (VER-P1-01 slice 1). Every query is scoped to tenant and
/// subject. Creation takes a per-(tenant, subject) advisory lock so the per-subject cap cannot be
/// exceeded by concurrent requests, and writes its audit event in the same transaction.
/// </summary>
public sealed class EfTenantSavedSearchStore : ITenantSavedSearchStore
{
    private const string UniqueViolation = "23505";
    public const string CreatedAction = "saved_search.created";
    public const string DeletedAction = "saved_search.deleted";
    public const string AuditEntityType = "saved_search";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantSavedSearchStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<SavedSearchCreateResult> CreateAsync(
        Guid tenantId, string subject, TenantSavedSearchDefinition definition, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"saved_search:{tenantId:D}:{subject}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        if (await _db.TenantSavedSearches.AsNoTracking().CountAsync(
                x => x.TenantId == tenantId && x.Subject == subject, cancellationToken)
            >= TenantSavedSearchLimits.MaxPerSubject)
        {
            return new SavedSearchCreateResult(SavedSearchCreateOutcome.LimitReached, null);
        }

        var row = new TenantSavedSearch
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            Subject = subject,
            Name = definition.Name,
            Query = definition.Query,
            SourceEntryId = definition.SourceEntryId,
            Jurisdiction = definition.Jurisdiction,
            SourceType = definition.SourceType,
            Freshness = definition.Freshness,
            ParserName = definition.ParserName,
            ParserVersion = definition.ParserVersion,
            SubjectTerm = definition.SubjectTerm,
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };
        _db.TenantSavedSearches.Add(row);
        _db.TenantAuditEvents.Add(NewAudit(CreatedAction, row, row.CreatedAtUtc));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            return new SavedSearchCreateResult(SavedSearchCreateOutcome.DuplicateName, null);
        }

        return new SavedSearchCreateResult(SavedSearchCreateOutcome.Created, ToModel(row));
    }

    public async Task<IReadOnlyList<TenantSavedSearchModel>> ListAsync(
        Guid tenantId, string subject, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        var rows = await _db.TenantSavedSearches.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Subject == subject)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<bool> DeleteAsync(Guid tenantId, string subject, Guid id, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        var row = await _db.TenantSavedSearches.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Subject == subject && x.Id == id, cancellationToken);
        if (row is null)
        {
            return false;
        }

        _db.TenantSavedSearches.Remove(row);
        _db.TenantAuditEvents.Add(NewAudit(DeletedAction, row, ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent removal already deleted the row: report it as absent.
            _db.ChangeTracker.Clear();
            return false;
        }

        return true;
    }

    private static TenantAuditEvent NewAudit(string action, TenantSavedSearch row, DateTime at)
    {
        var id = Guid.NewGuid();
        return new TenantAuditEvent
        {
            TenantId = row.TenantId,
            Id = id,
            EventKey = $"{action}:{id:D}",
            Action = action,
            EntityType = AuditEntityType,
            EntityId = row.Id.ToString("D"),
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

    private static TenantSavedSearchModel ToModel(TenantSavedSearch row) =>
        new(row.Id,
            new TenantSavedSearchDefinition(
                row.Name, row.Query, row.SourceEntryId, row.Jurisdiction, row.SourceType, row.Freshness,
                row.ParserName, row.ParserVersion, row.SubjectTerm),
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
