using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-03 slice 2 (#87): immutable tenant-scoped raw retrieval ledger.
/// Database-backed tests run against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_rawledger</c> database) and
/// are inconclusive otherwise; input validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantRawRetrievalLedgerTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime RetrievedAt = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("<html>public record 1</html>");

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var ledger = new EfTenantRawRetrievalLedger(null!, TimeProvider.System);
        var source = Guid.NewGuid();
        var cases = new (Guid Tenant, RawRetrievalInput Input)[]
        {
            (Guid.Empty, Input(source)),
            (TenantA, Input(source) with { IdempotencyKey = " " }),
            (TenantA, Input(source) with { IdempotencyKey = new string('k', TenantRawRetrievalLimits.MaxIdempotencyKeyLength + 1) }),
            (TenantA, Input(source) with { FinalUri = "" }),
            (TenantA, Input(source) with { FinalUri = "https://x.test/" + new string('a', TenantRawRetrievalLimits.MaxFinalUriLength) }),
            (TenantA, Input(source) with { RawBytes = new byte[TenantRawRetrievalLimits.MaxRawBytes + 1] }),
            (TenantA, Input(source) with { RetrievedAtUtc = DateTime.SpecifyKind(RetrievedAt, DateTimeKind.Local) }),
            (TenantA, Input(source) with { RetrievedAtUtc = DateTime.SpecifyKind(RetrievedAt, DateTimeKind.Unspecified) }),
        };
        foreach (var (tenant, input) in cases)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ledger.RecordAsync(tenant, input, CancellationToken.None));
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ledger.GetAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task RecordsBytesWithServerDigestAndIsTenantScoped()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var sourceB = await SourceAsync(db, TenantB, "https://example.test/b");
        var ledger = new EfTenantRawRetrievalLedger(db, TimeProvider.System);

        var result = await ledger.RecordAsync(TenantA, Input(sourceA), CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, result.Outcome);
        var record = result.Record!;
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Bytes)), record.ContentSha256);
        Assert.AreEqual(Bytes.LongLength, record.ByteLength);
        Assert.AreEqual(RetrievedAt, record.RetrievedAtUtc);
        Assert.AreEqual(sourceA, record.SourceEntryId);

        Assert.AreEqual(record, await ledger.GetAsync(TenantA, record.Id, CancellationToken.None));
        Assert.IsNull(await ledger.GetAsync(TenantB, record.Id, CancellationToken.None));
        Assert.IsNull(await ledger.GetAsync(TenantA, Guid.NewGuid(), CancellationToken.None));

        var stored = await db.TenantRawRetrievals.AsNoTracking().SingleAsync();
        CollectionAssert.AreEqual(Bytes, stored.RawBytes);
        Assert.IsNull(stored.EvidenceReference, "no pre-parser evidence reference");

        // Another tenant's source and an unknown source are indistinguishable.
        var crossTenant = await ledger.RecordAsync(TenantA, Input(sourceB) with { IdempotencyKey = "k2" }, CancellationToken.None);
        var unknown = await ledger.RecordAsync(TenantA, Input(Guid.NewGuid()) with { IdempotencyKey = "k3" }, CancellationToken.None);
        Assert.AreEqual(new RawRetrievalRecordResult(RawRetrievalRecordOutcome.SourceNotFound, null), crossTenant);
        Assert.AreEqual(crossTenant, unknown);
        Assert.AreEqual(1, await db.TenantRawRetrievals.CountAsync());
    }

    [TestMethod]
    public async Task IdempotencyKeyReplaysOnlyTheSameBytes()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var sourceA2 = await SourceAsync(db, TenantA, "https://example.test/a2");
        var sourceB = await SourceAsync(db, TenantB, "https://example.test/b");
        var ledger = new EfTenantRawRetrievalLedger(db, TimeProvider.System);

        var first = await ledger.RecordAsync(TenantA, Input(sourceA), CancellationToken.None);
        var replay = await ledger.RecordAsync(TenantA, Input(sourceA), CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Record, replay.Record);

        var conflicts = new[]
        {
            Input(sourceA) with { RawBytes = Encoding.UTF8.GetBytes("changed") },
            Input(sourceA) with { FinalUri = "https://example.test/other" },
            Input(sourceA2),
        };
        foreach (var conflict in conflicts)
        {
            Assert.AreEqual(
                new RawRetrievalRecordResult(RawRetrievalRecordOutcome.IdempotencyConflict, null),
                await ledger.RecordAsync(TenantA, conflict, CancellationToken.None));
        }

        // The same key in another tenant is a distinct record.
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded,
            (await ledger.RecordAsync(TenantB, Input(sourceB), CancellationToken.None)).Outcome);
        Assert.AreEqual(2, await db.TenantRawRetrievals.CountAsync());
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
            return await new EfTenantRawRetrievalLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Input(source), CancellationToken.None);
        }));

        Assert.AreEqual(1, results.Count(r => r.Outcome == RawRetrievalRecordOutcome.Recorded));
        Assert.IsTrue(results.All(r => r.Outcome is RawRetrievalRecordOutcome.Recorded or RawRetrievalRecordOutcome.Replayed));
        Assert.AreEqual(1, results.Select(r => r.Record!.Id).Distinct().Count());
        await using var check = new VeritasAtlasDbContext(options);
        Assert.AreEqual(1, await check.TenantRawRetrievals.CountAsync());
    }

    [TestMethod]
    public async Task SchemaRefusesForgedDigestsCrossTenantLinksReferencesAndMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "https://example.test/a");
        var record = (await new EfTenantRawRetrievalLedger(db, TimeProvider.System)
            .RecordAsync(TenantA, Input(sourceA), CancellationToken.None)).Record!;
        var digest = record.ContentSha256;

        Task Insert(Guid tenant, Guid sourceId, string sha, string? reference, long length) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_raw_retrievals (tenant_id, id, source_entry_id, idempotency_key, final_uri, raw_bytes, content_sha256, byte_length, evidence_reference, retrieved_at_utc, created_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {sourceId}, {Guid.NewGuid().ToString()}, {"https://example.test/a"}, {Bytes}, {sha}, {length}, {reference}, {RetrievedAt}, {RetrievedAt})");

        var forged = new string('0', 64);
        await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(TenantA, sourceA, forged, null, Bytes.Length));
        await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(TenantA, sourceA, digest, null, Bytes.Length + 1));
        await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(TenantA, sourceA, digest, "ev1:forged", Bytes.Length));
        await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(TenantB, sourceA, digest, null, Bytes.Length));
        await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(Guid.Empty, sourceA, digest, null, Bytes.Length));

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_raw_retrievals SET final_uri = 'https://evil.test/'",
                     "UPDATE tenant_raw_retrievals SET evidence_reference = NULL",
                     "DELETE FROM tenant_raw_retrievals",
                     "TRUNCATE tenant_raw_retrievals"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        // The source of a recorded retrieval cannot be deleted either.
        await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenant_source_registry_entries WHERE tenant_id = {TenantA} AND id = {sourceA}"));

        var stored = await db.TenantRawRetrievals.AsNoTracking().SingleAsync();
        Assert.AreEqual(record.Id, stored.Id);
        Assert.AreEqual("https://example.test/a", stored.FinalUri);
    }

    private static RawRetrievalInput Input(Guid source) =>
        new(source, "fetch-1", "https://example.test/a", RetrievedAt, Bytes);

    private static async Task<Guid> SourceAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var entry = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, key, CancellationToken.None);
        return entry!.Id;
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
        builder.Database += "_rawledger";
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
