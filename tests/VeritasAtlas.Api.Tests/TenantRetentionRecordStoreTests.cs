using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-18 slice 3 (#300): append-only retention records of a raw retrieval. SYNTHETIC data only. The
/// database tests run against a real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own
/// <c>*_retention</c> database) and are inconclusive otherwise; input validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantRetentionRecordStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private const string Actor = "synthetic-admin";
    private static readonly DateTime Soon = new(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var store = new EfTenantRetentionRecordStore(null!, TimeProvider.System);
        var id = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GetLatestAsync(Guid.Empty, id, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AppendAsync(Guid.Empty, id, true, null, Actor, CancellationToken.None));
        foreach (var actor in new[] { "", "  ", "a\nb", "a\u007f", new string('x', TenantAuditEventLimits.MaxSubjectLength + 1) })
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AppendAsync(TenantA, id, true, null, actor, CancellationToken.None), actor);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.AppendAsync(TenantA, id, false, DateTime.SpecifyKind(Soon, DateTimeKind.Unspecified), Actor, CancellationToken.None));
    }

    [TestMethod]
    public async Task AppendsGaplessVersionsReplaysIdenticalRequestsAndNeverShortensTheWindow()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var retrieval = await SeedAsync(db, TenantA, "a");
        var store = new EfTenantRetentionRecordStore(db, TimeProvider.System);

        Assert.IsNull(await store.GetLatestAsync(TenantA, retrieval, CancellationToken.None));
        var first = await store.AppendAsync(TenantA, retrieval, false, Soon, Actor, CancellationToken.None);
        Assert.AreEqual((RetentionRecordAppendOutcome.Appended, 1), (first.Outcome, first.Record!.Version));
        Assert.AreEqual(Soon, first.Record.RetainUntilUtc);

        var replay = await store.AppendAsync(TenantA, retrieval, false, Soon, Actor, CancellationToken.None);
        Assert.AreEqual((RetentionRecordAppendOutcome.NoChange, 1), (replay.Outcome, replay.Record!.Version));

        var hold = await store.AppendAsync(TenantA, retrieval, true, Soon, Actor, CancellationToken.None);
        Assert.AreEqual((RetentionRecordAppendOutcome.Appended, 2), (hold.Outcome, hold.Record!.Version), "a hold toggles");
        var later = await store.AppendAsync(TenantA, retrieval, false, Soon.AddYears(1), Actor, CancellationToken.None);
        Assert.AreEqual((RetentionRecordAppendOutcome.Appended, 3), (later.Outcome, later.Record!.Version), "the window may extend");

        foreach (var until in new DateTime?[] { Soon, null })
        {
            var refused = await store.AppendAsync(TenantA, retrieval, false, until, Actor, CancellationToken.None);
            Assert.AreEqual(RetentionRecordAppendOutcome.WindowShortened, refused.Outcome);
        }

        var latest = await store.GetLatestAsync(TenantA, retrieval, CancellationToken.None);
        Assert.AreEqual((3, false, Soon.AddYears(1)), (latest!.Version, latest.LegalHold, latest.RetainUntilUtc));
        Assert.AreEqual(3, await db.TenantRetentionRecords.AsNoTracking().CountAsync());
        Assert.AreEqual(3, await db.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "retention.recorded"),
            "one audit event per appended version, none for replays and refusals");
    }

    [TestMethod]
    public async Task ForeignAndUnknownRetrievalsAreNotFoundAndTenantsAreIsolated()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var a = await SeedAsync(db, TenantA, "a");
        var store = new EfTenantRetentionRecordStore(db, TimeProvider.System);

        Assert.AreEqual(RetentionRecordAppendOutcome.RetrievalNotFound,
            (await store.AppendAsync(TenantB, a, true, null, Actor, CancellationToken.None)).Outcome);
        Assert.AreEqual(RetentionRecordAppendOutcome.RetrievalNotFound,
            (await store.AppendAsync(TenantA, Guid.NewGuid(), true, null, Actor, CancellationToken.None)).Outcome);
        Assert.AreEqual(0, await db.TenantRetentionRecords.AsNoTracking().CountAsync());

        Assert.AreEqual(RetentionRecordAppendOutcome.Appended,
            (await store.AppendAsync(TenantA, a, true, null, Actor, CancellationToken.None)).Outcome, "positive control");
        Assert.IsNull(await store.GetLatestAsync(TenantB, a, CancellationToken.None), "another tenant reads nothing");
    }

    [TestMethod]
    public async Task DatabaseRefusesTamperingGapsForeignParentsAndShorteningOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var retrieval = await SeedAsync(db, TenantA, "a");
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        async Task<string?> Run(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            try { await cmd.ExecuteNonQueryAsync(); return null; }
            catch (PostgresException ex) { return ex.SqlState; }
        }

        string Insert(Guid tenant, Guid retrievalId, int version, string until) =>
            $"INSERT INTO tenant_raw_retrieval_retention_records (tenant_id, raw_retrieval_id, version, legal_hold, retain_until_utc, actor_subject, created_at_utc) " +
            $"VALUES ('{tenant}', '{retrievalId}', {version}, false, {until}, 'synthetic', now())";

        Assert.IsNull(await Run(Insert(TenantA, retrieval, 1, "'2031-01-01Z'")), "positive control");
        Assert.AreEqual("23514", await Run(Insert(TenantA, retrieval, 3, "'2031-01-01Z'")), "gap");
        Assert.AreEqual("23514", await Run(Insert(TenantA, retrieval, 1, "'2032-01-01Z'")), "duplicate version is not previous + 1");
        Assert.AreEqual("23514", await Run(Insert(TenantA, retrieval, 2, "'2030-01-01Z'")), "earlier window");
        Assert.AreEqual("23514", await Run(Insert(TenantA, retrieval, 2, "NULL")), "cleared window");
        Assert.AreEqual("23514", await Run(Insert(TenantB, retrieval, 1, "NULL")), "foreign tenant parent");
        Assert.AreEqual("23514", await Run(Insert(TenantA, Guid.NewGuid(), 1, "NULL")), "unknown parent");
        Assert.AreEqual("55000", await Run("UPDATE tenant_raw_retrieval_retention_records SET legal_hold = true"));
        Assert.AreEqual("55000", await Run("DELETE FROM tenant_raw_retrieval_retention_records"));
        Assert.AreEqual("55000", await Run("TRUNCATE tenant_raw_retrieval_retention_records"));
        Assert.AreEqual(1, await db.TenantRetentionRecords.AsNoTracking().CountAsync());
    }

    [TestMethod]
    public async Task ConcurrentAppendsCannotForkTheVersionChain()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var seed = new VeritasAtlasDbContext(Options(cs));
        var retrieval = await SeedAsync(seed, TenantA, "a");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = new VeritasAtlasDbContext(Options(cs));
            return await new EfTenantRetentionRecordStore(db, TimeProvider.System)
                .AppendAsync(TenantA, retrieval, i % 2 == 0, null, Actor + i, CancellationToken.None);
        }));

        var appended = results.Count(r => r.Outcome == RetentionRecordAppendOutcome.Appended);
        Assert.IsTrue(results.All(r => r.Outcome is RetentionRecordAppendOutcome.Appended
            or RetentionRecordAppendOutcome.NoChange or RetentionRecordAppendOutcome.Conflict));
        Assert.IsTrue(appended >= 1);
        var versions = await seed.TenantRetentionRecords.AsNoTracking().Select(x => x.Version).OrderBy(v => v).ToListAsync();
        CollectionAssert.AreEqual(Enumerable.Range(1, versions.Count).ToList(), versions, "gapless, no fork");
        Assert.AreEqual(appended, versions.Count);
        Assert.AreEqual(appended, await seed.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "retention.recorded"));
    }

    private static async Task<Guid> SeedAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var at = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var recorded = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant,
            new RawRetrievalInput(source!.Id, key, "https://www.legislation.gov.uk/synthetic/act/1/data.xml", at, Encoding.UTF8.GetBytes("<doc>" + key + "</doc>")),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, recorded.Outcome);
        return recorded.Record!.Id;
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
        builder.Database += "_retention";
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
