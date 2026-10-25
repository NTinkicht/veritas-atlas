using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-03 slice 12 (#107): the <c>tenant_retrieval_attestations</c> schema
/// alone (controlled vocabularies, exact-text shapes, same-tenant raw
/// retrieval link, append-only). Runs against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_attestschema</c> database) and
/// is inconclusive otherwise.
/// </summary>
[TestClass]
public sealed class TenantRetrievalAttestationSchemaTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime AttestedAt = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("<html>public record 1</html>");

    [TestMethod]
    public async Task FreshMigrationCreatesTheTableAndKeepsRawRetrievalsUnverified()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        await RetrievalAsync(db, TenantA, "r1");
        Assert.AreEqual(0, await db.TenantRetrievalAttestations.CountAsync());
        Assert.IsTrue((await db.TenantRawRetrievals.AsNoTracking().ToListAsync()).All(x => x.EvidenceReference is null));
        Assert.IsTrue(TenantRetrievalAttestationLimits.MaxIdentityLength > 0);
    }

    [TestMethod]
    public async Task AttestationKeyIsUniquePerTenantOnly()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrievalA = await RetrievalAsync(db, TenantA, "r-a");
        var retrievalB = await RetrievalAsync(db, TenantB, "r-b");

        Task Insert(Guid tenant, Guid retrieval, string key = "att-1") =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_retrieval_attestations (tenant_id, id, raw_retrieval_id, attestation_key, retriever_identity, publisher_identity, binding_mechanism, binding_reference, authentication_result, jurisdiction, source_type, attested_at_utc, created_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {retrieval}, {key}, {"veritas-retriever/1"}, {"Cour supreme"}, {"source_registry"}, {"registry:1"}, {"authenticated"}, {"dz"}, {"court_record"}, {AttestedAt}, {AttestedAt})");

        await Insert(TenantA, retrievalA);
        await Insert(TenantB, retrievalB);
        var duplicate = await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(TenantA, retrievalA));
        Assert.AreEqual("23505", duplicate.SqlState);

        // The idempotency key is stored exactly as given, like the other text
        // columns: never blank, never padded, no control characters.
        foreach (var key in new[] { "", "   ", " att-2", "att-2 ", "att\u00072", "att\n2", "att\u00852" })
        {
            var refused = await Assert.ThrowsExactlyAsync<PostgresException>(() => Insert(TenantA, retrievalA, key), key);
            Assert.AreEqual("23514", refused.SqlState, key);
        }

        Assert.AreEqual(2, await db.TenantRetrievalAttestations.CountAsync());
    }

    [TestMethod]
    public async Task SchemaRefusesInvalidVocabulariesShapesCrossTenantLinksAndMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrievalA = await RetrievalAsync(db, TenantA, "r-a");

        Task Insert(Guid tenant, Guid retrieval, string retriever, string mechanism, string reference, string result, string jurisdiction, string sourceType) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_retrieval_attestations (tenant_id, id, raw_retrieval_id, attestation_key, retriever_identity, publisher_identity, binding_mechanism, binding_reference, authentication_result, jurisdiction, source_type, attested_at_utc, created_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {retrieval}, {Guid.NewGuid().ToString()}, {retriever}, {"Cour supreme"}, {mechanism}, {reference}, {result}, {jurisdiction}, {sourceType}, {AttestedAt}, {AttestedAt})");

        await Insert(TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "court_record");
        await Insert(TenantA, retrievalA, "veritas-retriever/1", "publisher_signature", "sig:1", "unauthenticated", "dz.alger", "filing");

        var refused = new (Guid Tenant, Guid Retrieval, string Retriever, string Mechanism, string Reference, string Result, string Jurisdiction, string SourceType)[]
        {
            (TenantA, retrievalA, "veritas-retriever/1", "tls_only", "registry:1", "authenticated", "dz", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "verified", "dz", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "news"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "DZ", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "", "court_record"),
            (TenantA, retrievalA, " veritas", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
            (TenantA, retrievalA, "veritas\u0007", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "", "authenticated", "dz", "court_record"),
            // Another tenant's retrieval, an empty tenant.
            (TenantB, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
            (Guid.Empty, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
        };
        foreach (var row in refused)
        {
            await Assert.ThrowsExactlyAsync<PostgresException>(
                () => Insert(row.Tenant, row.Retrieval, row.Retriever, row.Mechanism, row.Reference, row.Result, row.Jurisdiction, row.SourceType),
                row.ToString());
        }

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_retrieval_attestations SET authentication_result = 'authenticated'",
                     "DELETE FROM tenant_retrieval_attestations",
                     "TRUNCATE tenant_retrieval_attestations"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        Assert.AreEqual(2, await db.TenantRetrievalAttestations.CountAsync());
    }

    private static async Task<Guid> RetrievalAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var result = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, "https://example.test/a", AttestedAt, Bytes), "synthetic-seeder", CancellationToken.None);
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
        builder.Database += "_attestschema";
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
