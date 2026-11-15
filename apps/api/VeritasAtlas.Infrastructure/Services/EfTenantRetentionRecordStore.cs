using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

public sealed class EfTenantRetentionRecordStore : ITenantRetentionRecordStore
{
    public const string RecordedAction = "retention.recorded";
    public const string AuditEntityType = "raw_retrieval";

    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantRetentionRecordStore(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<TenantRetentionRecordModel?> GetLatestAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant id is required.", nameof(tenantId));
        return ToModel(await LatestAsync(tenantId, rawRetrievalId, cancellationToken));
    }

    public async Task<RetentionRecordAppendResult> AppendAsync(
        Guid tenantId, Guid rawRetrievalId, bool legalHold, DateTime? retainUntilUtc, string actorSubject,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
            throw new ArgumentException("A usable actor subject is required.", nameof(actorSubject));
        if (retainUntilUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("The retain-until instant must be UTC.", nameof(retainUntilUtc));

        var retainUntil = retainUntilUtc is { } r ? ToPostgresPrecision(r) : (DateTime?)null;
        if (!await _db.TenantRawRetrievals.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == rawRetrievalId, cancellationToken))
            return new(RetentionRecordAppendOutcome.RetrievalNotFound, null);

        var latest = await LatestAsync(tenantId, rawRetrievalId, cancellationToken);
        if (latest is not null && latest.LegalHold == legalHold && latest.RetainUntilUtc == retainUntil)
            return new(RetentionRecordAppendOutcome.NoChange, ToModel(latest));
        if (latest?.RetainUntilUtc is { } previous && (retainUntil is null || retainUntil < previous))
            return new(RetentionRecordAppendOutcome.WindowShortened, null);

        var at = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var record = new TenantRetentionRecord
        {
            TenantId = tenantId, RawRetrievalId = rawRetrievalId, Version = (latest?.Version ?? 0) + 1,
            LegalHold = legalHold, RetainUntilUtc = retainUntil, ActorSubject = actorSubject, CreatedAtUtc = at
        };
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId, Id = Guid.NewGuid(),
            EventKey = $"{RecordedAction}:{rawRetrievalId:D}:{record.Version}",
            Action = RecordedAction, EntityType = AuditEntityType, EntityId = rawRetrievalId.ToString("D"),
            ActorSubject = actorSubject, OccurredAtUtc = at
        };

        // One SaveChanges, one transaction: a version never exists without its audit event.
        _db.TenantRetentionRecords.Add(record);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" or "23514" })
        {
            // A concurrent writer took this version (or shortened the window first): the caller re-reads.
            _db.Entry(record).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            return new(RetentionRecordAppendOutcome.Conflict, null);
        }

        return new(RetentionRecordAppendOutcome.Appended, ToModel(record));
    }

    private Task<TenantRetentionRecord?> LatestAsync(Guid tenantId, Guid rawRetrievalId, CancellationToken ct) =>
        _db.TenantRetentionRecords.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RawRetrievalId == rawRetrievalId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);

    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantRetentionRecordModel? ToModel(TenantRetentionRecord? row) =>
        row is null ? null : new(row.Version, row.LegalHold,
            row.RetainUntilUtc is { } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : null,
            row.ActorSubject, DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
