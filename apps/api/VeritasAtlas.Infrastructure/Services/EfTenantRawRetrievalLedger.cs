using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed immutable raw retrieval ledger (VER-P0-03 slice 2, #87).
/// Bytes, digest and metadata are written in one INSERT; the database re-checks
/// the digest, refuses UPDATE/DELETE and keeps the evidence reference NULL.
/// Each newly recorded row writes its <c>tenant_raw_retrieval.recorded</c>
/// audit event in the same transaction (#101); replays and refusals write none.
/// </summary>
public sealed class EfTenantRawRetrievalLedger : ITenantRawRetrievalLedger
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    public const string RecordedAction = "tenant_raw_retrieval.recorded";
    public const string AuditEntityType = "tenant_raw_retrieval";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantRawRetrievalLedger(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<RawRetrievalRecordResult> RecordAsync(
        Guid tenantId, RawRetrievalInput input, string actorSubject, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        Validate(input);
        RequireActor(actorSubject);
        var digest = Convert.ToHexStringLower(SHA256.HashData(input.RawBytes));

        var sourceExists = await _db.TenantSourceRegistryEntries.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == input.SourceEntryId, cancellationToken);
        if (!sourceExists)
        {
            return new RawRetrievalRecordResult(RawRetrievalRecordOutcome.SourceNotFound, null);
        }

        var existing = await FindByKeyAsync(tenantId, input.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, input, digest);
        }

        var row = new TenantRawRetrieval
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            SourceEntryId = input.SourceEntryId,
            IdempotencyKey = input.IdempotencyKey,
            FinalUri = input.FinalUri,
            RawBytes = input.RawBytes.ToArray(),
            ContentSha256 = digest,
            ByteLength = input.RawBytes.LongLength,
            RetrievedAtUtc = ToPostgresPrecision(input.RetrievedAtUtc),
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };
        // Same SaveChanges, so one transaction: the raw row never exists without
        // its audit event, nor the event without it (#101). The event carries
        // no digest, URI or bytes.
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{RecordedAction}:{row.Id:D}",
            Action = RecordedAction,
            EntityType = AuditEntityType,
            EntityId = row.Id.ToString("D"),
            ActorSubject = actorSubject,
            OccurredAtUtc = row.CreatedAtUtc
        };

        _db.TenantRawRetrievals.Add(row);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation or ForeignKeyViolation } pg)
        {
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            if (pg.SqlState == ForeignKeyViolation)
            {
                return new RawRetrievalRecordResult(RawRetrievalRecordOutcome.SourceNotFound, null);
            }

            // A concurrent writer won the idempotency key: replay against it.
            var winner = await FindByKeyAsync(tenantId, input.IdempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency key winner is not visible.", ex);
            return Replay(winner, input, digest);
        }

        return new RawRetrievalRecordResult(RawRetrievalRecordOutcome.Recorded, ToModel(row));
    }

    public async Task<TenantRawRetrievalModel?> GetAsync(
        Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var row = await _db.TenantRawRetrievals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    private Task<TenantRawRetrieval?> FindByKeyAsync(
        Guid tenantId, string idempotencyKey, CancellationToken cancellationToken) =>
        _db.TenantRawRetrievals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, cancellationToken);

    private static RawRetrievalRecordResult Replay(TenantRawRetrieval existing, RawRetrievalInput input, string digest)
    {
        var same = existing.ContentSha256 == digest
            && existing.SourceEntryId == input.SourceEntryId
            && existing.FinalUri == input.FinalUri;
        return same
            ? new RawRetrievalRecordResult(RawRetrievalRecordOutcome.Replayed, ToModel(existing))
            : new RawRetrievalRecordResult(RawRetrievalRecordOutcome.IdempotencyConflict, null);
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    // Same rule as the audit ledger's actor check
    // (ck_tenant_audit_events_actor_subject_shape): a typed refusal before the
    // database, never a raw DbUpdateException.
    private static void RequireActor(string actorSubject)
    {
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid actor subject.", nameof(actorSubject));
        }
    }

    private static void Validate(RawRetrievalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RawBytes);
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey)
            || input.IdempotencyKey.Length > TenantRawRetrievalLimits.MaxIdempotencyKeyLength)
        {
            throw new ArgumentException("Invalid idempotency key.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.FinalUri)
            || input.FinalUri.Length > TenantRawRetrievalLimits.MaxFinalUriLength)
        {
            throw new ArgumentException("Invalid final URI.", nameof(input));
        }

        if (input.RawBytes.Length > TenantRawRetrievalLimits.MaxRawBytes)
        {
            throw new ArgumentException("Raw content is too large.", nameof(input));
        }

        if (input.RetrievedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Retrieval time must be UTC.", nameof(input));
        }
    }

    // PostgreSQL stores microseconds: round-trip-stable values keep a recorded
    // result identical to a later read or replay of the same row.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantRawRetrievalModel ToModel(TenantRawRetrieval row) =>
        new(row.Id, row.SourceEntryId, row.IdempotencyKey, row.FinalUri, row.ContentSha256, row.ByteLength,
            DateTime.SpecifyKind(row.RetrievedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
