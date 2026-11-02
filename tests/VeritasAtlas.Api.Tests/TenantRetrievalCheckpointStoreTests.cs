using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 follow-up (#167): the tenant-scoped retrieval checkpoint store. Database-backed tests
/// run against a real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_checkpoint</c>
/// database) and are inconclusive otherwise; argument validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantRetrievalCheckpointStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime T0 = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task EmptyIdentifiersAreRefusedBeforeTheDatabaseIsTouched()
    {
        await using var db = new VeritasAtlasDbContext(new DbContextOptionsBuilder<VeritasAtlasDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1").Options);
        var store = new EfTenantRetrievalCheckpointStore(db);
        var id = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GetAsync(Guid.Empty, id, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GetAsync(TenantA, Guid.Empty, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AdvanceAsync(Guid.Empty, id, 1, id, T0, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AdvanceAsync(TenantA, Guid.Empty, 1, id, T0, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AdvanceAsync(TenantA, id, 1, Guid.Empty, T0, default));
    }

    [TestMethod]
    public async Task CheckpointAdvancesOneStepAtATimeAndReplaysIdempotently()
    {
        var cs = Database();
        await ResetAsync(cs!);
        await using var db = new VeritasAtlasDbContext(Options(cs!));
        var (source, r1) = await SourceWithRetrievalAsync(db, TenantA, "k1");
        var r2 = await RetrievalAsync(db, TenantA, source, "k2");
        var r3 = await RetrievalAsync(db, TenantA, source, "k3");
        var store = new EfTenantRetrievalCheckpointStore(db);

        Assert.IsNull(await store.GetAsync(TenantA, source, default));
        Assert.AreEqual("sequence_gap", (await store.AdvanceAsync(TenantA, source, 2, r2, T0, default)).Reason);

        var first = await store.AdvanceAsync(TenantA, source, 1, r1, T0, default);
        Assert.AreEqual(RetrievalCheckpointAdvanceOutcome.Advanced, first.Outcome);
        Assert.AreEqual("k1", first.Checkpoint!.Key);
        Assert.AreEqual(RetrievalCheckpointAdvanceOutcome.Replayed, (await store.AdvanceAsync(TenantA, source, 1, r1, T0, default)).Outcome);
        Assert.AreEqual(RetrievalCheckpointAdvanceOutcome.Advanced, (await store.AdvanceAsync(TenantA, source, 2, r2, T0.AddMinutes(1), default)).Outcome);

        Assert.AreEqual("regression", (await store.AdvanceAsync(TenantA, source, 1, r1, T0, default)).Reason);
        Assert.AreEqual("sequence_gap", (await store.AdvanceAsync(TenantA, source, 4, r3, T0.AddMinutes(2), default)).Reason);
        Assert.AreEqual("time_regression", (await store.AdvanceAsync(TenantA, source, 3, r3, T0, default)).Reason);
        // Same sequence, different retrieval: refused, nothing changes.
        var conflict = await store.AdvanceAsync(TenantA, source, 2, r3, T0.AddMinutes(1), default);
        Assert.AreEqual(RetrievalCheckpointAdvanceOutcome.Refused, conflict.Outcome);
        Assert.AreEqual("sequence_key_conflict", conflict.Reason);

        var now = await store.GetAsync(TenantA, source, default);
        Assert.AreEqual(2, now!.Sequence);
        Assert.AreEqual("k2", now.Key);
        Assert.AreEqual(r2, now.RawRetrievalId);
    }

    [TestMethod]
    public async Task CheckpointCannotPointAtAnotherTenantsOrSourcesRetrievalNorBeAheadOfPersistence()
    {
        var cs = Database();
        await ResetAsync(cs!);
        await using var db = new VeritasAtlasDbContext(Options(cs!));
        var (sourceA, rA) = await SourceWithRetrievalAsync(db, TenantA, "ka");
        var (sourceB, rB) = await SourceWithRetrievalAsync(db, TenantB, "kb");
        var (sourceA2, rA2) = await SourceWithRetrievalAsync(db, TenantA, "ka2");
        var store = new EfTenantRetrievalCheckpointStore(db);

        foreach (var (tenant, source, retrieval) in new[]
                 { (TenantA, sourceA, rB), (TenantB, sourceB, rA), (TenantA, sourceA, rA2), (TenantA, sourceA, Guid.NewGuid()) })
        {
            Assert.AreEqual(RetrievalCheckpointAdvanceOutcome.RetrievalNotFound,
                (await store.AdvanceAsync(tenant, source, 1, retrieval, T0, default)).Outcome);
        }

        Assert.AreEqual(0, await db.TenantRetrievalCheckpoints.CountAsync());
        Assert.AreEqual(RetrievalCheckpointAdvanceOutcome.Advanced, (await store.AdvanceAsync(TenantA, sourceA, 1, rA, T0, default)).Outcome);
        Assert.IsNull(await store.GetAsync(TenantB, sourceA, default));
        Assert.IsNull(await store.GetAsync(TenantA, sourceA2, default));
    }

    [TestMethod]
    public async Task RacingWritersCannotBothAdvanceTheSameSequence()
    {
        var cs = Database();
        await ResetAsync(cs!);
        Guid source, r1, r2a, r2b;
        await using (var seed = new VeritasAtlasDbContext(Options(cs!)))
        {
            (source, r1) = await SourceWithRetrievalAsync(seed, TenantA, "k1");
            r2a = await RetrievalAsync(seed, TenantA, source, "k2a");
            r2b = await RetrievalAsync(seed, TenantA, source, "k2b");
            await new EfTenantRetrievalCheckpointStore(seed).AdvanceAsync(TenantA, source, 1, r1, T0, default);
        }

        async Task<RetrievalCheckpointAdvanceResult> Race(Guid retrieval)
        {
            await using var db = new VeritasAtlasDbContext(Options(cs!));
            return await new EfTenantRetrievalCheckpointStore(db).AdvanceAsync(TenantA, source, 2, retrieval, T0.AddMinutes(1), default);
        }

        var results = await Task.WhenAll(Race(r2a), Race(r2b));
        Assert.AreEqual(1, results.Count(r => r.Outcome == RetrievalCheckpointAdvanceOutcome.Advanced));
        await using var check = new VeritasAtlasDbContext(Options(cs!));
        Assert.AreEqual(2, (await new EfTenantRetrievalCheckpointStore(check).GetAsync(TenantA, source, default))!.Sequence);
    }

    [TestMethod]
    public async Task DatabaseRefusesEverythingButAOneStepAdvance()
    {
        var cs = Database();
        await ResetAsync(cs!);
        await using var db = new VeritasAtlasDbContext(Options(cs!));
        var (source, r1) = await SourceWithRetrievalAsync(db, TenantA, "k1");
        await new EfTenantRetrievalCheckpointStore(db).AdvanceAsync(TenantA, source, 1, r1, T0, default);

        foreach (var sql in new[]
                 {
                     "UPDATE tenant_retrieval_checkpoints SET sequence = 3",
                     "UPDATE tenant_retrieval_checkpoints SET sequence = 2, completed_at_utc = completed_at_utc - interval '1 minute'",
                     "UPDATE tenant_retrieval_checkpoints SET sequence = 2, source_entry_id = gen_random_uuid()",
                     "DELETE FROM tenant_retrieval_checkpoints",
                     "TRUNCATE tenant_retrieval_checkpoints"
                 })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.IsTrue(ex.SqlState is "55000" or "23503", sql + " -> " + ex.SqlState);
        }

        Assert.AreEqual(1, (await new EfTenantRetrievalCheckpointStore(db).GetAsync(TenantA, source, default))!.Sequence);
    }

    private static async Task<(Guid Source, Guid Retrieval)> SourceWithRetrievalAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        return (source!.Id, await RetrievalAsync(db, tenant, source.Id, key));
    }

    private static async Task<Guid> RetrievalAsync(VeritasAtlasDbContext db, Guid tenant, Guid source, string key)
    {
        var result = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source, key, "https://example.test/a", T0, Encoding.UTF8.GetBytes("<html>" + key + "</html>")),
            "synthetic-seeder", CancellationToken.None);
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
        builder.Database += "_checkpoint";
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
