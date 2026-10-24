using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed append-only fetch-attempt ledger (VER-P0-03 slice 3, #89).
/// The database enforces the outcome shape, the same-tenant same-source link to
/// the raw retrieval and refuses UPDATE/DELETE/TRUNCATE; this service checks the
/// same rules first so callers get a typed outcome rather than a raw error.
/// </summary>
public sealed class EfTenantFetchAttemptLedger : ITenantFetchAttemptLedger
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    private const string RetrievalConstraint = "fk_tenant_fetch_attempts_tenant_source_raw_retrieval";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantFetchAttemptLedger(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<FetchAttemptRecordResult> RecordAsync(
        Guid tenantId, FetchAttemptInput input, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        Validate(input);

        var sourceExists = await _db.TenantSourceRegistryEntries.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == input.SourceEntryId, cancellationToken);
        if (!sourceExists)
        {
            return new FetchAttemptRecordResult(FetchAttemptRecordOutcome.SourceNotFound, null);
        }

        if (input.RawRetrievalId is { } retrievalId)
        {
            var retrievalExists = await _db.TenantRawRetrievals.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.SourceEntryId == input.SourceEntryId && x.Id == retrievalId,
                    cancellationToken);
            if (!retrievalExists)
            {
                return new FetchAttemptRecordResult(FetchAttemptRecordOutcome.RetrievalNotFound, null);
            }
        }

        var row = new TenantFetchAttempt
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            SourceEntryId = input.SourceEntryId,
            AttemptKey = input.AttemptKey,
            RequestedUri = input.RequestedUri,
            Outcome = ToDb(input.Outcome),
            FailureClass = input.FailureClass is { } failure ? ToDb(failure) : null,
            HttpStatus = input.HttpStatus,
            RawRetrievalId = input.RawRetrievalId,
            AttemptedAtUtc = ToPostgresPrecision(input.AttemptedAtUtc),
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        var existing = await FindByKeyAsync(tenantId, input.AttemptKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, row);
        }

        _db.TenantFetchAttempts.Add(row);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation or ForeignKeyViolation } pg)
        {
            _db.Entry(row).State = EntityState.Detached;
            if (pg.SqlState == ForeignKeyViolation)
            {
                return new FetchAttemptRecordResult(
                    pg.ConstraintName == RetrievalConstraint
                        ? FetchAttemptRecordOutcome.RetrievalNotFound
                        : FetchAttemptRecordOutcome.SourceNotFound,
                    null);
            }

            // A concurrent writer won the attempt key: replay against it.
            var winner = await FindByKeyAsync(tenantId, input.AttemptKey, cancellationToken)
                ?? throw new InvalidOperationException("Attempt key winner is not visible.", ex);
            return Replay(winner, row);
        }

        return new FetchAttemptRecordResult(FetchAttemptRecordOutcome.Recorded, ToModel(row));
    }

    public async Task<IReadOnlyList<TenantFetchAttemptModel>> ListForSourceAsync(
        Guid tenantId, Guid sourceEntryId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var rows = await _db.TenantFetchAttempts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SourceEntryId == sourceEntryId)
            .OrderBy(x => x.AttemptedAtUtc).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<TenantFetchAttempt?> FindByKeyAsync(
        Guid tenantId, string attemptKey, CancellationToken cancellationToken) =>
        _db.TenantFetchAttempts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.AttemptKey == attemptKey, cancellationToken);

    private static FetchAttemptRecordResult Replay(TenantFetchAttempt existing, TenantFetchAttempt candidate)
    {
        var same = existing.SourceEntryId == candidate.SourceEntryId
            && existing.RequestedUri == candidate.RequestedUri
            && existing.AttemptedAtUtc == candidate.AttemptedAtUtc
            && existing.Outcome == candidate.Outcome
            && existing.FailureClass == candidate.FailureClass
            && existing.HttpStatus == candidate.HttpStatus
            && existing.RawRetrievalId == candidate.RawRetrievalId;
        return same
            ? new FetchAttemptRecordResult(FetchAttemptRecordOutcome.Replayed, ToModel(existing))
            : new FetchAttemptRecordResult(FetchAttemptRecordOutcome.IdempotencyConflict, null);
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static void Validate(FetchAttemptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.AttemptKey)
            || input.AttemptKey.Length > TenantFetchAttemptLimits.MaxAttemptKeyLength)
        {
            throw new ArgumentException("Invalid attempt key.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.RequestedUri)
            || input.RequestedUri.Length > TenantFetchAttemptLimits.MaxRequestedUriLength)
        {
            throw new ArgumentException("Invalid requested URI.", nameof(input));
        }

        if (input.AttemptedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Attempt time must be UTC.", nameof(input));
        }

        if (!Enum.IsDefined(input.Outcome)
            || (input.FailureClass is { } failureClass && !Enum.IsDefined(failureClass)))
        {
            throw new ArgumentException("Unknown outcome or failure class.", nameof(input));
        }

        if (input.HttpStatus is < 100 or > 599)
        {
            throw new ArgumentException("HTTP status must be between 100 and 599.", nameof(input));
        }

        var shapeIsValid = input.Outcome == FetchAttemptOutcome.Succeeded
            ? input.RawRetrievalId is { } id && id != Guid.Empty && input.FailureClass is null
            : input.RawRetrievalId is null && input.FailureClass is not null
                && (input.FailureClass != FetchFailureClass.HttpStatus || input.HttpStatus is not null);
        if (!shapeIsValid)
        {
            throw new ArgumentException(
                "A success needs a raw retrieval and no failure class; a failure needs a failure class, no raw retrieval, and an HTTP status when the class is http_status.",
                nameof(input));
        }
    }

    private static string ToDb(FetchAttemptOutcome outcome) => outcome switch
    {
        FetchAttemptOutcome.Succeeded => "succeeded",
        FetchAttemptOutcome.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private static string ToDb(FetchFailureClass failure) => failure switch
    {
        FetchFailureClass.Network => "network",
        FetchFailureClass.Timeout => "timeout",
        FetchFailureClass.HttpStatus => "http_status",
        FetchFailureClass.SizeLimit => "size_limit",
        FetchFailureClass.PolicyDenied => "policy_denied",
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    private static FetchAttemptOutcome OutcomeFromDb(string value) => value switch
    {
        "succeeded" => FetchAttemptOutcome.Succeeded,
        "failed" => FetchAttemptOutcome.Failed,
        _ => throw new InvalidOperationException("Unknown stored fetch outcome.")
    };

    private static FetchFailureClass FailureFromDb(string value) => value switch
    {
        "network" => FetchFailureClass.Network,
        "timeout" => FetchFailureClass.Timeout,
        "http_status" => FetchFailureClass.HttpStatus,
        "size_limit" => FetchFailureClass.SizeLimit,
        "policy_denied" => FetchFailureClass.PolicyDenied,
        _ => throw new InvalidOperationException("Unknown stored fetch failure class.")
    };

    // PostgreSQL stores microseconds: round-trip-stable values keep a recorded
    // result identical to a later read or replay of the same row.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantFetchAttemptModel ToModel(TenantFetchAttempt row) =>
        new(row.Id, row.SourceEntryId, row.AttemptKey, row.RequestedUri,
            DateTime.SpecifyKind(row.AttemptedAtUtc, DateTimeKind.Utc),
            OutcomeFromDb(row.Outcome),
            row.FailureClass is null ? null : FailureFromDb(row.FailureClass),
            row.HttpStatus,
            row.RawRetrievalId,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
