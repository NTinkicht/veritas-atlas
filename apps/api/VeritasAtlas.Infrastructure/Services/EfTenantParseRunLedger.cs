using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed append-only parse-run ledger (VER-P0-03 slice 4, #91).
/// The database enforces the outcome shape, the same-tenant link to the raw
/// retrieval, a null evidence reference, and refuses UPDATE/DELETE/TRUNCATE;
/// this service checks the same rules first so callers get a typed outcome
/// rather than a raw error. It never writes an evidence reference.
/// Each newly recorded run writes its <c>tenant_parse_run.recorded</c> audit
/// event in the same transaction (#105).
/// </summary>
public sealed partial class EfTenantParseRunLedger : ITenantParseRunLedger
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    public const string RecordedAction = "tenant_parse_run.recorded";
    public const string AuditEntityType = "tenant_parse_run";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantParseRunLedger(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<ParseRunRecordResult> RecordAsync(
        Guid tenantId, ParseRunInput input, string actorSubject, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        Validate(input);
        // Same rule as the audit ledger's actor check
        // (ck_tenant_audit_events_actor_subject_shape): a typed refusal before
        // the database, never a raw DbUpdateException.
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid actor subject.", nameof(actorSubject));
        }

        var retrievalExists = await _db.TenantRawRetrievals.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == input.RawRetrievalId, cancellationToken);
        if (!retrievalExists)
        {
            return new ParseRunRecordResult(ParseRunRecordOutcome.RetrievalNotFound, null);
        }

        var row = new TenantParseRun
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            RawRetrievalId = input.RawRetrievalId,
            RunKey = input.RunKey,
            ParserName = input.ParserName,
            ParserVersion = input.ParserVersion,
            Outcome = ToDb(input.Outcome),
            FailureClass = input.FailureClass is { } failure ? ToDb(failure) : null,
            StartedAtUtc = ToPostgresPrecision(input.StartedAtUtc),
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        var existing = await FindByKeyAsync(tenantId, input.RunKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, row);
        }

        // Same SaveChanges, so one transaction: the run never exists without
        // its audit event, nor the event without it (#105). The event carries
        // no parser identity, outcome, failure class or retrieval id.
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

        _db.TenantParseRuns.Add(row);
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
                return new ParseRunRecordResult(ParseRunRecordOutcome.RetrievalNotFound, null);
            }

            // A concurrent writer won the run key: replay against it.
            var winner = await FindByKeyAsync(tenantId, input.RunKey, cancellationToken)
                ?? throw new InvalidOperationException("Run key winner is not visible.", ex);
            return Replay(winner, row);
        }

        return new ParseRunRecordResult(ParseRunRecordOutcome.Recorded, ToModel(row));
    }

    public async Task<IReadOnlyList<TenantParseRunModel>> ListForRetrievalAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var rows = await _db.TenantParseRuns.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RawRetrievalId == rawRetrievalId)
            .OrderBy(x => x.StartedAtUtc).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<TenantParseRun?> FindByKeyAsync(
        Guid tenantId, string runKey, CancellationToken cancellationToken) =>
        _db.TenantParseRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.RunKey == runKey, cancellationToken);

    private static ParseRunRecordResult Replay(TenantParseRun existing, TenantParseRun candidate)
    {
        var same = existing.RawRetrievalId == candidate.RawRetrievalId
            && existing.ParserName == candidate.ParserName
            && existing.ParserVersion == candidate.ParserVersion
            && existing.StartedAtUtc == candidate.StartedAtUtc
            && existing.Outcome == candidate.Outcome
            && existing.FailureClass == candidate.FailureClass;
        return same
            ? new ParseRunRecordResult(ParseRunRecordOutcome.Replayed, ToModel(existing))
            : new ParseRunRecordResult(ParseRunRecordOutcome.IdempotencyConflict, null);
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static void Validate(ParseRunInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RawRetrievalId == Guid.Empty)
        {
            throw new ArgumentException("A raw retrieval is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.RunKey)
            || input.RunKey.Length > TenantParseRunLimits.MaxRunKeyLength)
        {
            throw new ArgumentException("Invalid run key.", nameof(input));
        }

        if (!IsParserIdentity(input.ParserName) || !IsParserIdentity(input.ParserVersion))
        {
            throw new ArgumentException("Invalid parser name or version.", nameof(input));
        }

        if (input.StartedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Run start time must be UTC.", nameof(input));
        }

        if (!Enum.IsDefined(input.Outcome)
            || (input.FailureClass is { } failureClass && !Enum.IsDefined(failureClass)))
        {
            throw new ArgumentException("Unknown outcome or failure class.", nameof(input));
        }

        var shapeIsValid = input.Outcome == ParseRunOutcome.Failed
            ? input.FailureClass is not null
            : input.FailureClass is null;
        if (!shapeIsValid)
        {
            throw new ArgumentException(
                "A failed run needs a failure class; a parsed_unverified run must not have one.", nameof(input));
        }
    }

    private static bool IsParserIdentity(string? value) =>
        value is not null
        && value.Length <= TenantParseRunLimits.MaxParserIdentityLength
        && ParserIdentityPattern().IsMatch(value);

    // Same pattern as ck_tenant_parse_runs_parser_*_identity; \z refuses a
    // trailing newline that $ would accept.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ParserIdentityPattern();

    private static string ToDb(ParseRunOutcome outcome) => outcome switch
    {
        ParseRunOutcome.Failed => "failed",
        ParseRunOutcome.ParsedUnverified => "parsed_unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private static string ToDb(ParseFailureClass failure) => failure switch
    {
        ParseFailureClass.UnsupportedFormat => "unsupported_format",
        ParseFailureClass.Malformed => "malformed",
        ParseFailureClass.Oversized => "oversized",
        ParseFailureClass.ParserError => "parser_error",
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    private static ParseRunOutcome OutcomeFromDb(string value) => value switch
    {
        "failed" => ParseRunOutcome.Failed,
        "parsed_unverified" => ParseRunOutcome.ParsedUnverified,
        _ => throw new InvalidOperationException("Unknown stored parse outcome.")
    };

    private static ParseFailureClass FailureFromDb(string value) => value switch
    {
        "unsupported_format" => ParseFailureClass.UnsupportedFormat,
        "malformed" => ParseFailureClass.Malformed,
        "oversized" => ParseFailureClass.Oversized,
        "parser_error" => ParseFailureClass.ParserError,
        _ => throw new InvalidOperationException("Unknown stored parse failure class.")
    };

    // PostgreSQL stores microseconds: round-trip-stable values keep a recorded
    // result identical to a later read or replay of the same row.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantParseRunModel ToModel(TenantParseRun row) =>
        new(row.Id, row.RawRetrievalId, row.RunKey, row.ParserName, row.ParserVersion,
            DateTime.SpecifyKind(row.StartedAtUtc, DateTimeKind.Utc),
            OutcomeFromDb(row.Outcome),
            row.FailureClass is null ? null : FailureFromDb(row.FailureClass),
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
