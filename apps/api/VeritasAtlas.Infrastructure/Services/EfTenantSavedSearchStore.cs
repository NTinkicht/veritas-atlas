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
    public const string UpdatedAction = "saved_search.updated";
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
        _db.TenantSavedSearchVersions.Add(NewVersion(row));
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

    public async Task<SavedSearchUpdateResult> UpdateAsync(
        Guid tenantId, string subject, Guid id, int expectedVersion, TenantSavedSearchDefinition definition,
        CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"saved_search:{tenantId:D}:{subject}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        var row = await _db.TenantSavedSearches.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Subject == subject && x.Id == id, cancellationToken);
        if (row is null)
        {
            return new SavedSearchUpdateResult(SavedSearchUpdateOutcome.NotFound, null);
        }

        if (row.Version != expectedVersion)
        {
            return new SavedSearchUpdateResult(SavedSearchUpdateOutcome.VersionConflict, null);
        }

        if (ToDefinition(row) == definition)
        {
            return new SavedSearchUpdateResult(SavedSearchUpdateOutcome.Unchanged, ToModel(row));
        }

        if (row.Version >= TenantSavedSearchLimits.MaxVersions)
        {
            return new SavedSearchUpdateResult(SavedSearchUpdateOutcome.VersionLimitReached, null);
        }

        row.Name = definition.Name;
        row.Query = definition.Query;
        row.SourceEntryId = definition.SourceEntryId;
        row.Jurisdiction = definition.Jurisdiction;
        row.SourceType = definition.SourceType;
        row.Freshness = definition.Freshness;
        row.ParserName = definition.ParserName;
        row.ParserVersion = definition.ParserVersion;
        row.SubjectTerm = definition.SubjectTerm;
        row.Version += 1;
        var at = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var version = NewVersion(row);
        version.CreatedAtUtc = at;
        _db.TenantSavedSearchVersions.Add(version);
        _db.TenantAuditEvents.Add(NewAudit(UpdatedAction, row, at));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            return new SavedSearchUpdateResult(SavedSearchUpdateOutcome.DuplicateName, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row was deleted between the read and the write: report it as absent.
            _db.ChangeTracker.Clear();
            return new SavedSearchUpdateResult(SavedSearchUpdateOutcome.NotFound, null);
        }

        return new SavedSearchUpdateResult(SavedSearchUpdateOutcome.Updated, ToModel(row));
    }

    public async Task<IReadOnlyList<TenantSavedSearchVersionModel>?> ListVersionsAsync(
        Guid tenantId, string subject, Guid id, CancellationToken cancellationToken)
    {
        Require(tenantId, subject);
        var owned = await _db.TenantSavedSearches.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.Subject == subject && x.Id == id, cancellationToken);
        if (!owned)
        {
            return null;
        }

        var rows = await _db.TenantSavedSearchVersions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SavedSearchId == id)
            .OrderBy(x => x.Version)
            .ToListAsync(cancellationToken);
        return rows.Select(v => new TenantSavedSearchVersionModel(
            v.Version,
            new TenantSavedSearchDefinition(
                v.Name, v.Query, v.SourceEntryId, v.Jurisdiction, v.SourceType, v.Freshness,
                v.ParserName, v.ParserVersion, v.SubjectTerm),
            DateTime.SpecifyKind(v.CreatedAtUtc, DateTimeKind.Utc))).ToList();
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

    private static TenantSavedSearchVersion NewVersion(TenantSavedSearch row) => new()
    {
        TenantId = row.TenantId,
        SavedSearchId = row.Id,
        Version = row.Version,
        Name = row.Name,
        Query = row.Query,
        SourceEntryId = row.SourceEntryId,
        Jurisdiction = row.Jurisdiction,
        SourceType = row.SourceType,
        Freshness = row.Freshness,
        ParserName = row.ParserName,
        ParserVersion = row.ParserVersion,
        SubjectTerm = row.SubjectTerm,
        CreatedAtUtc = row.CreatedAtUtc
    };

    private static TenantSavedSearchDefinition ToDefinition(TenantSavedSearch row) =>
        new(row.Name, row.Query, row.SourceEntryId, row.Jurisdiction, row.SourceType, row.Freshness,
            row.ParserName, row.ParserVersion, row.SubjectTerm);

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
        new(row.Id, ToDefinition(row), DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc), row.Version);
}
