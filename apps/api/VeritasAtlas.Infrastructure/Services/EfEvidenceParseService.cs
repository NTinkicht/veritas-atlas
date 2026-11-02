using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Parsing;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// Runs <see cref="UkLegislationParserV1"/> over a tenant's immutable raw bytes and stores the
/// parse run, its chunks and the <c>tenant_parse_run.recorded</c> audit event in ONE
/// transaction (a single SaveChanges), so a run never exists without its chunks or the other
/// way round. The run key is derived from retrieval and parser identity, which makes a repeat
/// an idempotent replay. The source-native record id for this parser is the final URI path.
/// It never writes the parse-run or raw-retrieval evidence-reference slot and never verifies.
/// </summary>
public sealed class EfEvidenceParseService : IEvidenceParseService
{
    private const string UniqueViolation = "23505";
    private const int MaxElementIdLength = 256;
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfEvidenceParseService(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public static string RunKeyFor(Guid rawRetrievalId) =>
        $"parse:{rawRetrievalId:D}:{UkLegislationParserV1.Name}:{UkLegislationParserV1.Version}";

    public async Task<EvidenceParseResult> ParseAsync(
        Guid tenantId, Guid rawRetrievalId, string actorSubject, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (rawRetrievalId == Guid.Empty) throw new ArgumentException("A raw retrieval is required.", nameof(rawRetrievalId));
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
            throw new ArgumentException("Invalid actor subject.", nameof(actorSubject));

        var retrieval = await _db.TenantRawRetrievals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == rawRetrievalId, cancellationToken);
        if (retrieval is null)
            return new EvidenceParseResult(EvidenceParseOutcome.RetrievalNotFound, null, null, 0);

        var runKey = RunKeyFor(rawRetrievalId);
        var existing = await FindRunAsync(tenantId, runKey, cancellationToken);
        if (existing is not null) return await ReplayAsync(existing, cancellationToken);

        var (output, reason) = RunParser(retrieval);
        var now = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var run = new TenantParseRun
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            RawRetrievalId = rawRetrievalId,
            RunKey = runKey,
            ParserName = UkLegislationParserV1.Name,
            ParserVersion = UkLegislationParserV1.Version,
            Outcome = output.Succeeded ? "parsed_unverified" : "failed",
            FailureClass = output.Succeeded ? null : ToDb(output.Failure!.Value),
            StartedAtUtc = now,
            CreatedAtUtc = now
        };
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{EfTenantParseRunLedger.RecordedAction}:{run.Id:D}",
            Action = EfTenantParseRunLedger.RecordedAction,
            EntityType = EfTenantParseRunLedger.AuditEntityType,
            EntityId = run.Id.ToString("D"),
            ActorSubject = actorSubject,
            OccurredAtUtc = now
        };
        var chunks = output.Chunks.Select(c => new TenantEvidenceChunk
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            ParseRunId = run.Id,
            RawRetrievalId = rawRetrievalId,
            ParentEvidenceRef = c.ParentEvidenceRef,
            Ordinal = c.Ordinal,
            ElementId = c.ElementId,
            Text = c.Text,
            TextSha256 = c.TextSha256,
            CreatedAtUtc = now
        }).ToList();

        _db.TenantParseRuns.Add(run);
        _db.TenantAuditEvents.Add(audit);
        _db.TenantEvidenceChunks.AddRange(chunks);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // A concurrent writer won the run key: nothing of ours was committed.
            _db.Entry(run).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            foreach (var chunk in chunks) _db.Entry(chunk).State = EntityState.Detached;
            var winner = await FindRunAsync(tenantId, runKey, cancellationToken)
                ?? throw new InvalidOperationException("Run key winner is not visible.", ex);
            return await ReplayAsync(winner, cancellationToken);
        }

        return new EvidenceParseResult(
            output.Succeeded ? EvidenceParseOutcome.ParsedUnverified : EvidenceParseOutcome.Failed,
            ToModel(run), output.Succeeded ? null : reason, chunks.Count);
    }

    private static (ParserOutput Output, string? Reason) RunParser(TenantRawRetrieval retrieval)
    {
        if (!Uri.TryCreate(retrieval.FinalUri, UriKind.Absolute, out var uri)
            || uri.AbsolutePath.Trim('/').Length == 0)
            return Failed(ParserFailure.Malformed, "record_id_not_derivable");
        try
        {
            var output = UkLegislationParserV1.Parse(
                retrieval.RawBytes, retrieval.FinalUri, uri.AbsolutePath.Trim('/'), retrieval.ContentSha256);
            if (output.Succeeded && output.Chunks.Any(c => c.ElementId.Length > MaxElementIdLength))
                return Failed(ParserFailure.Malformed, "element_id_too_long");
            return (output, output.Reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Parser errors fail closed as a recorded failure; no exception text is kept.
            return (new ParserOutput(false, null, "parser_error", null, Array.Empty<EvidenceChunk>()), "parser_error");
        }

        static (ParserOutput, string?) Failed(ParserFailure failure, string reason) =>
            (ParserOutput.Fail(failure, reason), reason);
    }

    private async Task<EvidenceParseResult> ReplayAsync(TenantParseRun run, CancellationToken cancellationToken)
    {
        var count = await _db.TenantEvidenceChunks.AsNoTracking()
            .CountAsync(x => x.TenantId == run.TenantId && x.ParseRunId == run.Id, cancellationToken);
        return new EvidenceParseResult(EvidenceParseOutcome.Replayed, ToModel(run), null, count);
    }

    private Task<TenantParseRun?> FindRunAsync(Guid tenantId, string runKey, CancellationToken cancellationToken) =>
        _db.TenantParseRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.RunKey == runKey, cancellationToken);

    private static string ToDb(ParserFailure failure) => failure switch
    {
        ParserFailure.UnsupportedFormat => "unsupported_format",
        ParserFailure.Malformed => "malformed",
        ParserFailure.Oversized => "oversized",
        _ => "parser_error"
    };

    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantParseRunModel ToModel(TenantParseRun r) => new(
        r.Id, r.RawRetrievalId, r.RunKey, r.ParserName, r.ParserVersion,
        DateTime.SpecifyKind(r.StartedAtUtc, DateTimeKind.Utc),
        r.Outcome == "failed" ? ParseRunOutcome.Failed : ParseRunOutcome.ParsedUnverified,
        r.FailureClass switch
        {
            null => null,
            "unsupported_format" => ParseFailureClass.UnsupportedFormat,
            "malformed" => ParseFailureClass.Malformed,
            "oversized" => ParseFailureClass.Oversized,
            _ => ParseFailureClass.ParserError
        },
        DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc));
}
