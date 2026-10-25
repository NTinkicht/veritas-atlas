using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-03 slice 3 (#89): append-only tenant-scoped fetch-attempt ledger.
/// Database-backed tests run against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_fetchledger</c> database) and
/// are inconclusive otherwise; input validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantFetchAttemptLedgerTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime AttemptedAt = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("<html>public record 1</html>");
    private const string Actor = "synthetic-fetcher";

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var ledger = new EfTenantFetchAttemptLedger(null!, TimeProvider.System);
        var source = Guid.NewGuid();
        var retrieval = Guid.NewGuid();
        var cases = new (Guid Tenant, FetchAttemptInput Input)[]
        {
            (Guid.Empty, Failed(source)),
            (TenantA, Failed(source) with { AttemptKey = " " }),
            (TenantA, Failed(source) with { AttemptKey = new string('k', TenantFetchAttemptLimits.MaxAttemptKeyLength + 1) }),
            (TenantA, Failed(source) with { RequestedUri = "" }),
            (TenantA, Failed(source) with { RequestedUri = "https://x.test/" + new string('a', TenantFetchAttemptLimits.MaxRequestedUriLength) }),
            (TenantA, Failed(source) with { AttemptedAtUtc = DateTime.SpecifyKind(AttemptedAt, DateTimeKind.Local) }),
            (TenantA, Failed(source) with { AttemptedAtUtc = DateTime.SpecifyKind(AttemptedAt, DateTimeKind.Unspecified) }),
            (TenantA, Failed(source) with { Outcome = (FetchAttemptOutcome)7 }),
            (TenantA, Failed(source) with { FailureClass = (FetchFailureClass)42 }),
            (TenantA, Failed(source) with { HttpStatus = 99 }),
            (TenantA, Failed(source) with { HttpStatus = 600 }),
            // A failure needs a class, no retrieval, and a status for http_status.
            (TenantA, Failed(source) with { FailureClass = null }),
            (TenantA, Failed(source) with { RawRetrievalId = retrieval }),
            (TenantA, Failed(source) with { FailureClass = FetchFailureClass.HttpStatus, HttpStatus = null }),
            // A success needs a (non-empty) retrieval and no failure class.
            (TenantA, Succeeded(source, retrieval) with { RawRetrievalId = null }),
            (TenantA, Succeeded(source, retrieval) with { RawRetrievalId = Guid.Empty }),
            (TenantA, Succeeded(source, retrieval) with { FailureClass = FetchFailureClass.Network }),
        };
        foreach (var (tenant, input) in cases)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ledger.RecordAsync(tenant, input, Actor, CancellationToken.None), input.ToString());
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ledger.ListForSourceAsync(Guid.Empty, source, CancellationToken.None));
    }

    [TestMethod]
    public async Task RecordsFailedAndSucceededAttemptsTenantScoped()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var retrieval = await RetrievalAsync(db, TenantA, sourceA, "r1");
        var ledger = new EfTenantFetchAttemptLedger(db, TimeProvider.System);

        var failed = await ledger.RecordAsync(TenantA, Failed(sourceA), Actor, CancellationToken.None);
        Assert.AreEqual(FetchAttemptRecordOutcome.Recorded, failed.Outcome);
        Assert.AreEqual(FetchAttemptOutcome.Failed, failed.Attempt!.Outcome);
        Assert.AreEqual(FetchFailureClass.HttpStatus, failed.Attempt.FailureClass);
        Assert.AreEqual(503, failed.Attempt.HttpStatus);
        Assert.IsNull(failed.Attempt.RawRetrievalId);

        var succeeded = await ledger.RecordAsync(
            TenantA, Succeeded(sourceA, retrieval) with { AttemptedAtUtc = AttemptedAt.AddMinutes(5) }, Actor, CancellationToken.None);
        Assert.AreEqual(FetchAttemptRecordOutcome.Recorded, succeeded.Outcome);
        Assert.AreEqual(retrieval, succeeded.Attempt!.RawRetrievalId);
        Assert.IsNull(succeeded.Attempt.FailureClass);

        var stored = await db.TenantFetchAttempts.AsNoTracking().OrderBy(x => x.AttemptedAtUtc).ToListAsync();
        CollectionAssert.AreEqual(new[] { "failed", "succeeded" }, stored.Select(x => x.Outcome).ToArray());
        Assert.AreEqual("http_status", stored[0].FailureClass);

        var listed = await ledger.ListForSourceAsync(TenantA, sourceA, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { failed.Attempt, succeeded.Attempt }, listed.ToArray());
        Assert.AreEqual(0, (await ledger.ListForSourceAsync(TenantB, sourceA, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task CrossTenantAndCrossSourceLinksAreNotFound()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var sourceA2 = await SourceAsync(db, TenantA, "https://example.test/a2");
        var sourceB = await SourceAsync(db, TenantB, "https://example.test/b");
        var retrievalA2 = await RetrievalAsync(db, TenantA, sourceA2, "r-a2");
        var retrievalB = await RetrievalAsync(db, TenantB, sourceB, "r-b");
        var ledger = new EfTenantFetchAttemptLedger(db, TimeProvider.System);

        // Another tenant's source and an unknown source are indistinguishable.
        var crossTenantSource = await ledger.RecordAsync(TenantA, Failed(sourceB) with { AttemptKey = "k1" }, Actor, CancellationToken.None);
        var unknownSource = await ledger.RecordAsync(TenantA, Failed(Guid.NewGuid()) with { AttemptKey = "k2" }, Actor, CancellationToken.None);
        Assert.AreEqual(new FetchAttemptRecordResult(FetchAttemptRecordOutcome.SourceNotFound, null), crossTenantSource);
        Assert.AreEqual(crossTenantSource, unknownSource);

        // A success can only point at the same tenant's retrieval of the same source.
        foreach (var (retrieval, key) in new[] { (retrievalB, "k3"), (retrievalA2, "k4"), (Guid.NewGuid(), "k5") })
        {
            Assert.AreEqual(
                new FetchAttemptRecordResult(FetchAttemptRecordOutcome.RetrievalNotFound, null),
                await ledger.RecordAsync(TenantA, Succeeded(sourceA, retrieval) with { AttemptKey = key }, Actor, CancellationToken.None));
        }

        Assert.AreEqual(0, await db.TenantFetchAttempts.CountAsync());
    }

    [TestMethod]
    public async Task AttemptKeyReplaysOnlyTheSameAttempt()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var sourceA2 = await SourceAsync(db, TenantA, "https://example.test/a2");
        var sourceB = await SourceAsync(db, TenantB, "https://example.test/b");
        var ledger = new EfTenantFetchAttemptLedger(db, TimeProvider.System);

        var first = await ledger.RecordAsync(TenantA, Failed(sourceA), Actor, CancellationToken.None);
        var replay = await ledger.RecordAsync(TenantA, Failed(sourceA), Actor, CancellationToken.None);
        Assert.AreEqual(FetchAttemptRecordOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Attempt, replay.Attempt);

        var conflicts = new[]
        {
            Failed(sourceA) with { HttpStatus = 500 },
            Failed(sourceA) with { FailureClass = FetchFailureClass.Timeout, HttpStatus = null },
            Failed(sourceA) with { RequestedUri = "https://example.test/other" },
            Failed(sourceA) with { AttemptedAtUtc = AttemptedAt.AddSeconds(1) },
            Failed(sourceA2),
        };
        foreach (var conflict in conflicts)
        {
            Assert.AreEqual(
                new FetchAttemptRecordResult(FetchAttemptRecordOutcome.IdempotencyConflict, null),
                await ledger.RecordAsync(TenantA, conflict, Actor, CancellationToken.None), conflict.ToString());
        }

        // The same key in another tenant is a distinct attempt.
        Assert.AreEqual(FetchAttemptRecordOutcome.Recorded,
            (await ledger.RecordAsync(TenantB, Failed(sourceB), Actor, CancellationToken.None)).Outcome);
        Assert.AreEqual(2, await db.TenantFetchAttempts.CountAsync());
    }

    [TestMethod]
    public async Task ConcurrentSameKeyWritesYieldOneRow()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);
        Guid source;
        await using (var setup = new VeritasAtlasDbContext(options))
        {
            source = await SourceAsync(setup, TenantA, "https://example.test/race");
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantFetchAttemptLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Failed(source), Actor, CancellationToken.None);
        }));

        Assert.AreEqual(1, results.Count(r => r.Outcome == FetchAttemptRecordOutcome.Recorded));
        Assert.IsTrue(results.All(r => r.Outcome is FetchAttemptRecordOutcome.Recorded or FetchAttemptRecordOutcome.Replayed));
        Assert.AreEqual(1, results.Select(r => r.Attempt!.Id).Distinct().Count());
        await using var check = new VeritasAtlasDbContext(options);
        Assert.AreEqual(1, await check.TenantFetchAttempts.CountAsync());
    }

    [TestMethod]
    public async Task SchemaRefusesInconsistentShapesCrossSourceLinksAndMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var sourceA2 = await SourceAsync(db, TenantA, "https://example.test/a2");
        var retrievalA = await RetrievalAsync(db, TenantA, sourceA, "r-a");
        var retrievalA2 = await RetrievalAsync(db, TenantA, sourceA2, "r-a2");

        Task Insert(Guid tenant, Guid sourceId, string outcome, string? failureClass, int? httpStatus, Guid? retrieval) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_fetch_attempts (tenant_id, id, source_entry_id, attempt_key, requested_uri, outcome, failure_class, http_status, raw_retrieval_id, attempted_at_utc, created_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {sourceId}, {Guid.NewGuid().ToString()}, {"https://example.test/a"}, {outcome}, {failureClass}, {httpStatus}, {retrieval}, {AttemptedAt}, {AttemptedAt})");

        // The two consistent shapes are accepted.
        await Insert(TenantA, sourceA, "failed", "network", null, null);
        await Insert(TenantA, sourceA, "succeeded", null, 200, retrievalA);

        var refused = new (Guid Tenant, Guid Source, string Outcome, string? Failure, int? Status, Guid? Retrieval)[]
        {
            (TenantA, sourceA, "unknown", "network", null, null),
            (TenantA, sourceA, "failed", "dns", null, null),
            (TenantA, sourceA, "failed", null, null, null),
            (TenantA, sourceA, "failed", "network", null, retrievalA),
            (TenantA, sourceA, "failed", "http_status", null, null),
            (TenantA, sourceA, "failed", "http_status", 99, null),
            (TenantA, sourceA, "succeeded", null, null, null),
            (TenantA, sourceA, "succeeded", "timeout", null, retrievalA),
            // Another source's retrieval, another tenant, an empty tenant.
            (TenantA, sourceA, "succeeded", null, null, retrievalA2),
            (TenantB, sourceA, "failed", "network", null, null),
            (Guid.Empty, sourceA, "failed", "network", null, null),
        };
        foreach (var row in refused)
        {
            await Assert.ThrowsExactlyAsync<PostgresException>(
                () => Insert(row.Tenant, row.Source, row.Outcome, row.Failure, row.Status, row.Retrieval), row.ToString());
        }

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_fetch_attempts SET outcome = 'failed'",
                     "UPDATE tenant_fetch_attempts SET requested_uri = 'https://evil.test/'",
                     "DELETE FROM tenant_fetch_attempts",
                     "TRUNCATE tenant_fetch_attempts"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        Assert.AreEqual(2, await db.TenantFetchAttempts.CountAsync());
    }

    [TestMethod]
    public async Task RecordedAttemptsWriteExactlyOneAuditEventEachInTheSameTenant()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var retrieval = await RetrievalAsync(db, TenantA, sourceA, "r1");
        var ledger = new EfTenantFetchAttemptLedger(db, TimeProvider.System);

        var failed = await ledger.RecordAsync(TenantA, Failed(sourceA), "synthetic-worker", CancellationToken.None);
        var succeeded = await ledger.RecordAsync(TenantA, Succeeded(sourceA, retrieval), Actor, CancellationToken.None);
        Assert.AreEqual(FetchAttemptRecordOutcome.Recorded, failed.Outcome);
        Assert.AreEqual(FetchAttemptRecordOutcome.Recorded, succeeded.Outcome);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        var events = await check.TenantAuditEvents.AsNoTracking()
            .Where(e => e.EntityType == EfTenantFetchAttemptLedger.AuditEntityType)
            .ToListAsync();
        Assert.AreEqual(2, events.Count);
        foreach (var (attempt, actor) in new[] { (failed.Attempt!, "synthetic-worker"), (succeeded.Attempt!, Actor) })
        {
            var audit = events.Single(e => e.EntityId == attempt.Id.ToString("D"));
            Assert.AreEqual(TenantA, audit.TenantId);
            Assert.AreEqual($"tenant_fetch_attempt.recorded:{attempt.Id:D}", audit.EventKey);
            Assert.AreEqual("tenant_fetch_attempt.recorded", audit.Action);
            Assert.AreEqual("tenant_fetch_attempt", audit.EntityType);
            Assert.AreEqual(actor, audit.ActorSubject);
            Assert.AreEqual(attempt.CreatedAtUtc, DateTime.SpecifyKind(audit.OccurredAtUtc, DateTimeKind.Utc));
        }

        // The event carries no URI, failure class or retrieval id.
        foreach (var audit in events)
        {
            var fields = string.Join("|", audit.EventKey, audit.Action, audit.EntityType, audit.EntityId, audit.ActorSubject);
            Assert.IsFalse(fields.Contains("example.test", StringComparison.Ordinal), fields);
            Assert.IsFalse(fields.Contains("http_status", StringComparison.Ordinal), fields);
            Assert.IsFalse(fields.Contains(retrieval.ToString("D"), StringComparison.Ordinal), fields);
        }

        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync(e => e.TenantId == TenantB));
    }

    [TestMethod]
    public async Task ReplaysConflictsAndRefusalsWriteNoAuditEvent()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var sourceA2 = await SourceAsync(db, TenantA, "https://example.test/a2");
        var sourceB = await SourceAsync(db, TenantB, "https://example.test/b");
        var retrievalA2 = await RetrievalAsync(db, TenantA, sourceA2, "r-a2");
        var ledger = new EfTenantFetchAttemptLedger(db, TimeProvider.System);
        Assert.AreEqual(FetchAttemptRecordOutcome.Recorded,
            (await ledger.RecordAsync(TenantA, Failed(sourceA), Actor, CancellationToken.None)).Outcome);

        var outcomes = new[]
        {
            await ledger.RecordAsync(TenantA, Failed(sourceA), Actor, CancellationToken.None),
            await ledger.RecordAsync(TenantA, Failed(sourceA) with { HttpStatus = 500 }, Actor, CancellationToken.None),
            await ledger.RecordAsync(TenantA, Failed(sourceB) with { AttemptKey = "k1" }, Actor, CancellationToken.None),
            await ledger.RecordAsync(TenantA, Failed(Guid.NewGuid()) with { AttemptKey = "k2" }, Actor, CancellationToken.None),
            await ledger.RecordAsync(TenantA, Succeeded(sourceA, retrievalA2) with { AttemptKey = "k3" }, Actor, CancellationToken.None),
        };
        CollectionAssert.AreEqual(
            new[]
            {
                FetchAttemptRecordOutcome.Replayed, FetchAttemptRecordOutcome.IdempotencyConflict,
                FetchAttemptRecordOutcome.SourceNotFound, FetchAttemptRecordOutcome.SourceNotFound,
                FetchAttemptRecordOutcome.RetrievalNotFound
            },
            outcomes.Select(o => o.Outcome).ToArray());

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(1, await check.TenantFetchAttempts.CountAsync());
        Assert.AreEqual(1, await check.TenantAuditEvents.CountAsync(e => e.EntityType == EfTenantFetchAttemptLedger.AuditEntityType));
    }

    [TestMethod]
    public async Task ConcurrentSameKeyWritesYieldOneAuditEvent()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);
        Guid source;
        await using (var setup = new VeritasAtlasDbContext(options))
        {
            source = await SourceAsync(setup, TenantA, "https://example.test/race");
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantFetchAttemptLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Failed(source), $"synthetic-worker-{i}", CancellationToken.None);
        }));

        var winner = results.Single(r => r.Outcome == FetchAttemptRecordOutcome.Recorded).Attempt!;
        await using var check = new VeritasAtlasDbContext(options);
        var audit = await check.TenantAuditEvents.AsNoTracking()
            .SingleAsync(e => e.EntityType == EfTenantFetchAttemptLedger.AuditEntityType);
        Assert.AreEqual(winner.Id.ToString("D"), audit.EntityId);
    }

    [TestMethod]
    public async Task FailedAuditWriteRollsBackTheAttempt()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        // Test-only fault in this per-test database: every later audit insert fails.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION test_refuse_audit() RETURNS trigger LANGUAGE plpgsql AS "
            + "$$BEGIN RAISE EXCEPTION 'audit unavailable' USING ERRCODE = 'P0001'; END$$");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER test_refuse_audit BEFORE INSERT ON tenant_audit_events "
            + "FOR EACH ROW EXECUTE FUNCTION test_refuse_audit()");

        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
            new EfTenantFetchAttemptLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Failed(sourceA), Actor, CancellationToken.None));
        Assert.AreEqual("P0001", (error.InnerException as PostgresException)?.SqlState);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(0, await check.TenantFetchAttempts.CountAsync());
        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync(e => e.EntityType == EfTenantFetchAttemptLedger.AuditEntityType));
    }

    [TestMethod]
    public async Task InvalidActorIsRefusedBeforeTheDatabaseIsTouched()
    {
        // No reachable server: a refusal must happen before any connection.
        var options = new DbContextOptionsBuilder<VeritasAtlasDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1").Options;
        await using var db = new VeritasAtlasDbContext(options);
        var ledger = new EfTenantFetchAttemptLedger(db, TimeProvider.System);

        foreach (var actor in new[] { "", "   ", "a\u0000b", "a\u007fb", "a\u0085b",
                     new string('a', TenantAuditEventLimits.MaxSubjectLength + 1) })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ledger.RecordAsync(TenantA, Failed(Guid.NewGuid()), actor, CancellationToken.None), actor);
        }
    }

    private static FetchAttemptInput Failed(Guid source) =>
        new(source, "attempt-1", "https://example.test/a", AttemptedAt,
            FetchAttemptOutcome.Failed, FetchFailureClass.HttpStatus, 503, null);

    private static FetchAttemptInput Succeeded(Guid source, Guid retrieval) =>
        new(source, "attempt-2", "https://example.test/a", AttemptedAt,
            FetchAttemptOutcome.Succeeded, null, 200, retrieval);

    private static async Task<Guid> SourceAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var entry = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, key, "synthetic-seeder", CancellationToken.None);
        return entry!.Id;
    }

    private static async Task<Guid> RetrievalAsync(VeritasAtlasDbContext db, Guid tenant, Guid source, string key)
    {
        var result = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source, key, "https://example.test/a", AttemptedAt, Bytes), "synthetic-seeder", CancellationToken.None);
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
        builder.Database += "_fetchledger";
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
