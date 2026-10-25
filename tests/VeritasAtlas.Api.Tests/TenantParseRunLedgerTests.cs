using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-03 slice 4 (#91): append-only tenant-scoped parser identity/run
/// history. Database-backed tests run against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_parseledger</c> database) and
/// are inconclusive otherwise; input validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantParseRunLedgerTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime StartedAt = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("<html>public record 1</html>");
    private const string Actor = "synthetic-parser";

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var ledger = new EfTenantParseRunLedger(null!, TimeProvider.System);
        var retrieval = Guid.NewGuid();
        var tooLongIdentity = new string('a', TenantParseRunLimits.MaxParserIdentityLength + 1);
        var cases = new (Guid Tenant, ParseRunInput Input)[]
        {
            (Guid.Empty, Failed(retrieval)),
            (TenantA, Failed(Guid.Empty)),
            (TenantA, Failed(retrieval) with { RunKey = " " }),
            (TenantA, Failed(retrieval) with { RunKey = new string('k', TenantParseRunLimits.MaxRunKeyLength + 1) }),
            (TenantA, Failed(retrieval) with { ParserName = "" }),
            (TenantA, Failed(retrieval) with { ParserName = "Html" }),
            (TenantA, Failed(retrieval) with { ParserName = "-html" }),
            (TenantA, Failed(retrieval) with { ParserName = "html parser" }),
            (TenantA, Failed(retrieval) with { ParserName = "html\n" }),
            (TenantA, Failed(retrieval) with { ParserName = tooLongIdentity }),
            (TenantA, Failed(retrieval) with { ParserVersion = "" }),
            (TenantA, Failed(retrieval) with { ParserVersion = ".1" }),
            (TenantA, Failed(retrieval) with { ParserVersion = "1.0/../x" }),
            (TenantA, Failed(retrieval) with { ParserVersion = tooLongIdentity }),
            (TenantA, Failed(retrieval) with { StartedAtUtc = DateTime.SpecifyKind(StartedAt, DateTimeKind.Local) }),
            (TenantA, Failed(retrieval) with { StartedAtUtc = DateTime.SpecifyKind(StartedAt, DateTimeKind.Unspecified) }),
            (TenantA, Failed(retrieval) with { Outcome = (ParseRunOutcome)7 }),
            (TenantA, Failed(retrieval) with { FailureClass = (ParseFailureClass)42 }),
            // A failure needs a class; an unverified parse must not have one.
            (TenantA, Failed(retrieval) with { FailureClass = null }),
            (TenantA, Unverified(retrieval) with { FailureClass = ParseFailureClass.Malformed }),
        };
        foreach (var (tenant, input) in cases)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ledger.RecordAsync(tenant, input, Actor, CancellationToken.None), input.ToString());
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ledger.ListForRetrievalAsync(Guid.Empty, retrieval, CancellationToken.None));
    }

    [TestMethod]
    public async Task KeepsTheRunHistoryOfTwoParserVersionsTenantScoped()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrieval = await RetrievalAsync(db, TenantA, "r1");
        var ledger = new EfTenantParseRunLedger(db, TimeProvider.System);

        var failed = await ledger.RecordAsync(TenantA, Failed(retrieval), Actor, CancellationToken.None);
        Assert.AreEqual(ParseRunRecordOutcome.Recorded, failed.Outcome);
        Assert.AreEqual(ParseRunOutcome.Failed, failed.Run!.Outcome);
        Assert.AreEqual(ParseFailureClass.Malformed, failed.Run.FailureClass);
        Assert.AreEqual("1.0.0", failed.Run.ParserVersion);

        var unverified = await ledger.RecordAsync(
            TenantA, Unverified(retrieval) with { StartedAtUtc = StartedAt.AddMinutes(5) }, Actor, CancellationToken.None);
        Assert.AreEqual(ParseRunRecordOutcome.Recorded, unverified.Outcome);
        Assert.AreEqual(ParseRunOutcome.ParsedUnverified, unverified.Run!.Outcome);
        Assert.AreEqual("1.1.0", unverified.Run.ParserVersion);
        Assert.IsNull(unverified.Run.FailureClass);

        var stored = await db.TenantParseRuns.AsNoTracking().OrderBy(x => x.StartedAtUtc).ToListAsync();
        CollectionAssert.AreEqual(new[] { "failed", "parsed_unverified" }, stored.Select(x => x.Outcome).ToArray());
        Assert.AreEqual("malformed", stored[0].FailureClass);
        // Nothing is ever minted or verified before the VER-P0-07 parser.
        Assert.IsTrue(stored.All(x => x.EvidenceReference is null));
        Assert.IsTrue((await db.TenantRawRetrievals.AsNoTracking().ToListAsync()).All(x => x.EvidenceReference is null));

        var listed = await ledger.ListForRetrievalAsync(TenantA, retrieval, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { failed.Run, unverified.Run }, listed.ToArray());
        Assert.AreEqual(0, (await ledger.ListForRetrievalAsync(TenantB, retrieval, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task CrossTenantAndUnknownRetrievalsAreNotFound()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrievalB = await RetrievalAsync(db, TenantB, "r-b");
        var ledger = new EfTenantParseRunLedger(db, TimeProvider.System);

        var crossTenant = await ledger.RecordAsync(TenantA, Failed(retrievalB) with { RunKey = "k1" }, Actor, CancellationToken.None);
        var unknown = await ledger.RecordAsync(TenantA, Failed(Guid.NewGuid()) with { RunKey = "k2" }, Actor, CancellationToken.None);
        Assert.AreEqual(new ParseRunRecordResult(ParseRunRecordOutcome.RetrievalNotFound, null), crossTenant);
        Assert.AreEqual(crossTenant, unknown);
        Assert.AreEqual(0, await db.TenantParseRuns.CountAsync());
    }

    [TestMethod]
    public async Task RunKeyReplaysOnlyTheSameRun()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrieval = await RetrievalAsync(db, TenantA, "r1");
        var retrieval2 = await RetrievalAsync(db, TenantA, "r2");
        var retrievalB = await RetrievalAsync(db, TenantB, "r-b");
        var ledger = new EfTenantParseRunLedger(db, TimeProvider.System);

        var first = await ledger.RecordAsync(TenantA, Failed(retrieval), Actor, CancellationToken.None);
        var replay = await ledger.RecordAsync(TenantA, Failed(retrieval), Actor, CancellationToken.None);
        Assert.AreEqual(ParseRunRecordOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Run, replay.Run);

        var conflicts = new[]
        {
            Failed(retrieval) with { FailureClass = ParseFailureClass.Oversized },
            Failed(retrieval) with { Outcome = ParseRunOutcome.ParsedUnverified, FailureClass = null },
            Failed(retrieval) with { ParserName = "pdf" },
            Failed(retrieval) with { ParserVersion = "1.0.1" },
            Failed(retrieval) with { StartedAtUtc = StartedAt.AddSeconds(1) },
            Failed(retrieval2),
        };
        foreach (var conflict in conflicts)
        {
            Assert.AreEqual(
                new ParseRunRecordResult(ParseRunRecordOutcome.IdempotencyConflict, null),
                await ledger.RecordAsync(TenantA, conflict, Actor, CancellationToken.None), conflict.ToString());
        }

        // The same key in another tenant is a distinct run.
        Assert.AreEqual(ParseRunRecordOutcome.Recorded,
            (await ledger.RecordAsync(TenantB, Failed(retrievalB), Actor, CancellationToken.None)).Outcome);
        Assert.AreEqual(2, await db.TenantParseRuns.CountAsync());
    }

    [TestMethod]
    public async Task ConcurrentSameKeyWritesYieldOneRow()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);
        Guid retrieval;
        await using (var setup = new VeritasAtlasDbContext(options))
        {
            retrieval = await RetrievalAsync(setup, TenantA, "race");
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantParseRunLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Unverified(retrieval), Actor, CancellationToken.None);
        }));

        Assert.AreEqual(1, results.Count(r => r.Outcome == ParseRunRecordOutcome.Recorded));
        Assert.IsTrue(results.All(r => r.Outcome is ParseRunRecordOutcome.Recorded or ParseRunRecordOutcome.Replayed));
        Assert.AreEqual(1, results.Select(r => r.Run!.Id).Distinct().Count());
        await using var check = new VeritasAtlasDbContext(options);
        Assert.AreEqual(1, await check.TenantParseRuns.CountAsync());
    }

    [TestMethod]
    public async Task SchemaRefusesEvidenceReferencesInconsistentShapesCrossTenantLinksAndMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrievalA = await RetrievalAsync(db, TenantA, "r-a");

        Task Insert(Guid tenant, Guid retrieval, string name, string version, string outcome, string? failureClass, string? evidenceReference) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_parse_runs (tenant_id, id, raw_retrieval_id, run_key, parser_name, parser_version, outcome, failure_class, evidence_reference, started_at_utc, created_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {retrieval}, {Guid.NewGuid().ToString()}, {name}, {version}, {outcome}, {failureClass}, {evidenceReference}, {StartedAt}, {StartedAt})");

        // The two consistent shapes are accepted.
        await Insert(TenantA, retrievalA, "html", "1.0.0", "failed", "parser_error", null);
        await Insert(TenantA, retrievalA, "html", "1.1.0", "parsed_unverified", null, null);

        var refused = new (Guid Tenant, Guid Retrieval, string Name, string Version, string Outcome, string? Failure, string? Evidence)[]
        {
            // No pre-parser evidence reference, whatever the outcome.
            (TenantA, retrievalA, "html", "1.1.0", "parsed_unverified", null, "ev1:0000000000000000"),
            (TenantA, retrievalA, "html", "1.0.0", "failed", "malformed", "ev1:0000000000000000"),
            (TenantA, retrievalA, "html", "1.0.0", "verified", null, null),
            (TenantA, retrievalA, "html", "1.0.0", "failed", "timeout", null),
            (TenantA, retrievalA, "html", "1.0.0", "failed", null, null),
            (TenantA, retrievalA, "html", "1.0.0", "parsed_unverified", "oversized", null),
            (TenantA, retrievalA, "HTML", "1.0.0", "failed", "malformed", null),
            (TenantA, retrievalA, " html", "1.0.0", "failed", "malformed", null),
            (TenantA, retrievalA, "html", "", "failed", "malformed", null),
            (TenantA, retrievalA, "html", "1.0.0\n", "failed", "malformed", null),
            // Another tenant's retrieval, an empty tenant.
            (TenantB, retrievalA, "html", "1.0.0", "failed", "malformed", null),
            (Guid.Empty, retrievalA, "html", "1.0.0", "failed", "malformed", null),
        };
        foreach (var row in refused)
        {
            await Assert.ThrowsExactlyAsync<PostgresException>(
                () => Insert(row.Tenant, row.Retrieval, row.Name, row.Version, row.Outcome, row.Failure, row.Evidence),
                row.ToString());
        }

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_parse_runs SET outcome = 'parsed_unverified', failure_class = NULL",
                     "UPDATE tenant_parse_runs SET parser_version = '9.9.9'",
                     "DELETE FROM tenant_parse_runs",
                     "TRUNCATE tenant_parse_runs"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        Assert.AreEqual(2, await db.TenantParseRuns.CountAsync());
    }

    [TestMethod]
    public async Task RecordedRunsWriteExactlyOneAuditEventEachInTheSameTenant()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrieval = await RetrievalAsync(db, TenantA, "r1");
        var ledger = new EfTenantParseRunLedger(db, TimeProvider.System);

        var failed = await ledger.RecordAsync(TenantA, Failed(retrieval), "synthetic-worker", CancellationToken.None);
        var unverified = await ledger.RecordAsync(TenantA, Unverified(retrieval), Actor, CancellationToken.None);
        Assert.AreEqual(ParseRunRecordOutcome.Recorded, failed.Outcome);
        Assert.AreEqual(ParseRunRecordOutcome.Recorded, unverified.Outcome);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        var events = await check.TenantAuditEvents.AsNoTracking()
            .Where(e => e.EntityType == EfTenantParseRunLedger.AuditEntityType)
            .ToListAsync();
        Assert.AreEqual(2, events.Count);
        foreach (var (run, actor) in new[] { (failed.Run!, "synthetic-worker"), (unverified.Run!, Actor) })
        {
            var audit = events.Single(e => e.EntityId == run.Id.ToString("D"));
            Assert.AreEqual(TenantA, audit.TenantId);
            Assert.AreEqual($"tenant_parse_run.recorded:{run.Id:D}", audit.EventKey);
            Assert.AreEqual("tenant_parse_run.recorded", audit.Action);
            Assert.AreEqual("tenant_parse_run", audit.EntityType);
            Assert.AreEqual(actor, audit.ActorSubject);
            Assert.AreEqual(run.CreatedAtUtc, DateTime.SpecifyKind(audit.OccurredAtUtc, DateTimeKind.Utc));
        }

        // The event carries no parser identity, outcome, failure class or retrieval id.
        foreach (var audit in events)
        {
            var fields = string.Join("|", audit.EventKey, audit.Action, audit.EntityType, audit.EntityId, audit.ActorSubject);
            foreach (var leaked in new[] { "html", "1.0.0", "1.1.0", "malformed", "parsed_unverified", "failed", retrieval.ToString("D") })
            {
                Assert.IsFalse(fields.Contains(leaked, StringComparison.Ordinal), fields);
            }
        }

        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync(e => e.TenantId == TenantB));
    }

    [TestMethod]
    public async Task ReplaysConflictsAndRefusalsWriteNoAuditEvent()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrievalA = await RetrievalAsync(db, TenantA, "r-a");
        var retrievalB = await RetrievalAsync(db, TenantB, "r-b");
        var ledger = new EfTenantParseRunLedger(db, TimeProvider.System);
        Assert.AreEqual(ParseRunRecordOutcome.Recorded,
            (await ledger.RecordAsync(TenantA, Failed(retrievalA), Actor, CancellationToken.None)).Outcome);

        var outcomes = new[]
        {
            await ledger.RecordAsync(TenantA, Failed(retrievalA), Actor, CancellationToken.None),
            await ledger.RecordAsync(TenantA, Failed(retrievalA) with { ParserVersion = "9.9.9" }, Actor, CancellationToken.None),
            await ledger.RecordAsync(TenantA, Failed(retrievalB) with { RunKey = "k1" }, Actor, CancellationToken.None),
            await ledger.RecordAsync(TenantA, Failed(Guid.NewGuid()) with { RunKey = "k2" }, Actor, CancellationToken.None),
        };
        CollectionAssert.AreEqual(
            new[]
            {
                ParseRunRecordOutcome.Replayed, ParseRunRecordOutcome.IdempotencyConflict,
                ParseRunRecordOutcome.RetrievalNotFound, ParseRunRecordOutcome.RetrievalNotFound
            },
            outcomes.Select(o => o.Outcome).ToArray());

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(1, await check.TenantParseRuns.CountAsync());
        Assert.AreEqual(1, await check.TenantAuditEvents.CountAsync(e => e.EntityType == EfTenantParseRunLedger.AuditEntityType));
    }

    [TestMethod]
    public async Task ConcurrentSameKeyWritesYieldOneAuditEvent()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);
        Guid retrieval;
        await using (var setup = new VeritasAtlasDbContext(options))
        {
            retrieval = await RetrievalAsync(setup, TenantA, "race");
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantParseRunLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Unverified(retrieval), $"synthetic-worker-{i}", CancellationToken.None);
        }));

        var winner = results.Single(r => r.Outcome == ParseRunRecordOutcome.Recorded).Run!;
        await using var check = new VeritasAtlasDbContext(options);
        var audit = await check.TenantAuditEvents.AsNoTracking()
            .SingleAsync(e => e.EntityType == EfTenantParseRunLedger.AuditEntityType);
        Assert.AreEqual(winner.Id.ToString("D"), audit.EntityId);
    }

    [TestMethod]
    public async Task FailedAuditWriteRollsBackTheRun()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrieval = await RetrievalAsync(db, TenantA, "r1");
        // Test-only fault in this per-test database: every later audit insert fails.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION test_refuse_audit() RETURNS trigger LANGUAGE plpgsql AS "
            + "$$BEGIN RAISE EXCEPTION 'audit unavailable' USING ERRCODE = 'P0001'; END$$");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER test_refuse_audit BEFORE INSERT ON tenant_audit_events "
            + "FOR EACH ROW EXECUTE FUNCTION test_refuse_audit()");

        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
            new EfTenantParseRunLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Failed(retrieval), Actor, CancellationToken.None));
        Assert.AreEqual("P0001", (error.InnerException as PostgresException)?.SqlState);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(0, await check.TenantParseRuns.CountAsync());
        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync(e => e.EntityType == EfTenantParseRunLedger.AuditEntityType));
    }

    [TestMethod]
    public async Task InvalidActorIsRefusedBeforeTheDatabaseIsTouched()
    {
        // No reachable server: a refusal must happen before any connection.
        var options = new DbContextOptionsBuilder<VeritasAtlasDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1").Options;
        await using var db = new VeritasAtlasDbContext(options);
        var ledger = new EfTenantParseRunLedger(db, TimeProvider.System);

        foreach (var actor in new[] { "", "   ", "a\u0000b", "a\u007fb", "a\u0085b",
                     new string('a', TenantAuditEventLimits.MaxSubjectLength + 1) })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ledger.RecordAsync(TenantA, Failed(Guid.NewGuid()), actor, CancellationToken.None), actor);
        }
    }

    private static ParseRunInput Failed(Guid retrieval) =>
        new(retrieval, "run-1", "html", "1.0.0", StartedAt, ParseRunOutcome.Failed, ParseFailureClass.Malformed);

    private static ParseRunInput Unverified(Guid retrieval) =>
        new(retrieval, "run-2", "html", "1.1.0", StartedAt, ParseRunOutcome.ParsedUnverified, null);

    private static async Task<Guid> RetrievalAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var result = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, "https://example.test/a", StartedAt, Bytes), "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, result.Outcome);
        return result.Record!.Id;
    }

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_parseledger";
        return builder.ConnectionString;
    }

    private static DbContextOptions<VeritasAtlasDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options;

    private static async Task ResetAsync(string connectionString)
    {
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }
}
