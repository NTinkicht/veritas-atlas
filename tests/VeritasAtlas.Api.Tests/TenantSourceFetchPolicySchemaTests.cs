using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-04 slice 5a (#119): the <c>tenant_source_fetch_policies</c> schema
/// alone (per-source unique revision and policy version, admission shapes,
/// same-tenant registry link, append-only). Runs against a real PostgreSQL
/// when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_fetchpolicyschema</c>
/// database) and is inconclusive otherwise.
/// </summary>
[TestClass]
public sealed class TenantSourceFetchPolicySchemaTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime CreatedAt = new(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc);

    private sealed record Row(
        Guid Tenant,
        Guid Source,
        int Revision = 1,
        string Version = "2026-10.1",
        string Jurisdiction = "dz",
        string SourceType = "court_record",
        string?[]? Hosts = null,
        string?[]? Schemes = null,
        int?[]? Ports = null,
        int MaxRedirects = 3,
        bool HttpException = false);

    [TestMethod]
    public async Task FreshMigrationCreatesAnEmptyTableAndRevisionsRoundTrip()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(0, await db.TenantSourceFetchPolicies.CountAsync());
        var source = await SourceAsync(db, TenantA, "s1");

        await InsertAsync(db, new Row(TenantA, source));
        await InsertAsync(db, new Row(TenantA, source, Revision: 2, Version: "2026-10.2",
            Hosts: ["records.example.dz", "archive.example.dz"], Schemes: ["https", "http"], Ports: [443, 80],
            MaxRedirects: 0, HttpException: true));

        var stored = await db.TenantSourceFetchPolicies.AsNoTracking()
            .Where(x => x.TenantId == TenantA && x.SourceRegistryEntryId == source)
            .OrderBy(x => x.Revision)
            .ToListAsync();
        Assert.AreEqual(2, stored.Count);
        CollectionAssert.AreEqual(new[] { "records.example.dz", "archive.example.dz" }, stored[1].AllowedHosts);
        CollectionAssert.AreEqual(new[] { "https", "http" }, stored[1].AllowedSchemes);
        CollectionAssert.AreEqual(new[] { 443, 80 }, stored[1].AllowedPorts);
        Assert.IsTrue(stored[1].PlaintextHttpException);
        Assert.AreEqual("2026-10.1", stored[0].PolicyVersion);
        Assert.IsFalse(stored[0].WideningApproved);
    }

    [TestMethod]
    public async Task RevisionAndPolicyVersionAreUniquePerSourceOnly()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA1 = await SourceAsync(db, TenantA, "a1");
        var sourceA2 = await SourceAsync(db, TenantA, "a2");
        var sourceB = await SourceAsync(db, TenantB, "b1");

        await InsertAsync(db, new Row(TenantA, sourceA1));
        // The same revision and version on another source or tenant is independent.
        await InsertAsync(db, new Row(TenantA, sourceA2));
        await InsertAsync(db, new Row(TenantB, sourceB));

        // Same revision with a new version (a concurrent writer), and a reused
        // version under a new revision (silent drift or reuse): both refused.
        foreach (var row in new[]
                 {
                     new Row(TenantA, sourceA1, Revision: 1, Version: "2026-10.2"),
                     new Row(TenantA, sourceA1, Revision: 2, Version: "2026-10.1"),
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => InsertAsync(db, row), row.ToString());
            Assert.AreEqual("23505", error.SqlState, row.ToString());
        }

        Assert.AreEqual(3, await db.TenantSourceFetchPolicies.CountAsync());
    }

    [TestMethod]
    public async Task SchemaRefusesInvalidShapesCrossTenantSourcesAndMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var sourceA = await SourceAsync(db, TenantA, "a1");
        await InsertAsync(db, new Row(TenantA, sourceA));

        var checkViolations = new[]
        {
            new Row(Guid.Empty, sourceA, Revision: 2, Version: "v2"),
            new Row(TenantA, sourceA, Revision: 0, Version: "v2"),
            new Row(TenantA, sourceA, Revision: 2, Version: "V2"),
            new Row(TenantA, sourceA, Revision: 2, Version: ""),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2\n"),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Jurisdiction: "DZ"),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Jurisdiction: "dz.alger"),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", SourceType: "Court"),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", SourceType: "1court"),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: []),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: [null]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: ["records.example.dz", null]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: ["Records.example.dz"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: [""]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: ["records.example.dz,evil.test"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: ["*.example.dz"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Hosts: ["records.example.dz:443"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2",
                Hosts: Enumerable.Range(0, 33).Select(i => (string?)$"h{i}.example.dz").ToArray()),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Schemes: []),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Schemes: ["ftp"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Schemes: ["HTTPS"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Schemes: ["https", null]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Schemes: ["https", "http", "https"], HttpException: true),
            // Plain http without the separately approved exception.
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Schemes: ["http"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Schemes: ["https", "http"]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Ports: []),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Ports: [0]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Ports: [443, 65536]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", Ports: [443, null]),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", MaxRedirects: -1),
            new Row(TenantA, sourceA, Revision: 2, Version: "v2", MaxRedirects: 11),
        };
        foreach (var row in checkViolations)
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => InsertAsync(db, row), Describe(row));
            Assert.AreEqual("23514", error.SqlState, Describe(row));
        }

        // Another tenant's source, an unknown source: no cross-tenant policy.
        foreach (var row in new[] { new Row(TenantB, sourceA), new Row(TenantA, Guid.NewGuid()) })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => InsertAsync(db, row), Describe(row));
            Assert.AreEqual("23503", error.SqlState, Describe(row));
        }

        // A multi-dimensional array is not a host list.
        var multi = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO tenant_source_fetch_policies (tenant_id, id, source_registry_entry_id, revision, policy_version, jurisdiction, source_type, allowed_hosts, allowed_schemes, allowed_ports, max_redirects, plaintext_http_exception, widening_approved, created_at_utc) VALUES ({TenantA}, {Guid.NewGuid()}, {sourceA}, 2, 'v2', 'dz', 'court_record', ARRAY[ARRAY['a.example.dz'], ARRAY['b.example.dz']], ARRAY['https'], ARRAY[443], 3, false, false, {CreatedAt})"));
        Assert.AreEqual("23514", multi.SqlState);

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_source_fetch_policies SET allowed_hosts = ARRAY['evil.example.dz']",
                     "UPDATE tenant_source_fetch_policies SET widening_approved = true",
                     "DELETE FROM tenant_source_fetch_policies",
                     "TRUNCATE tenant_source_fetch_policies"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        var survivor = await db.TenantSourceFetchPolicies.AsNoTracking().SingleAsync();
        CollectionAssert.AreEqual(new[] { "records.example.dz" }, survivor.AllowedHosts);
        Assert.IsFalse(survivor.WideningApproved);
    }

    private static string Describe(Row row) =>
        $"{row} hosts=[{string.Join('|', row.Hosts ?? [])}] schemes=[{string.Join('|', row.Schemes ?? [])}] ports=[{string.Join('|', row.Ports ?? [])}]";

    private static Task<int> InsertAsync(VeritasAtlasDbContext db, Row row)
    {
        var hosts = row.Hosts ?? ["records.example.dz"];
        var schemes = row.Schemes ?? ["https"];
        var ports = row.Ports ?? [443];
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO tenant_source_fetch_policies (tenant_id, id, source_registry_entry_id, revision, policy_version, jurisdiction, source_type, allowed_hosts, allowed_schemes, allowed_ports, max_redirects, plaintext_http_exception, widening_approved, created_at_utc) VALUES ({row.Tenant}, {Guid.NewGuid()}, {row.Source}, {row.Revision}, {row.Version}, {row.Jurisdiction}, {row.SourceType}, {hosts}, {schemes}, {ports}, {row.MaxRedirects}, {row.HttpException}, {false}, {CreatedAt})");
    }

    private static async Task<Guid> SourceAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://records.example.dz/" + key, "synthetic-seeder", CancellationToken.None);
        return source!.Id;
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
        builder.Database += "_fetchpolicyschema";
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
