using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-18 (#370): backup/restore drill. A migrated database holding tenant audit events is dumped
/// with <c>pg_dump</c> and restored with <c>pg_restore</c> into a fresh database; the restored copy
/// must carry identical rows and migration history, need no further migration, keep the append-only
/// triggers that make the ledger immutable, and still scope reads to the owning tenant. Runs against a
/// real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set and the PostgreSQL client tools are on
/// PATH; inconclusive otherwise (the CI workflow has no PostgreSQL service).
/// </summary>
[TestClass]
public sealed class BackupRestoreDrillTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    [TestMethod]
    public async Task RestoredDatabaseKeepsRowsMigrationHistoryAndImmutability()
    {
        if (Databases() is not { } db) return;

        await ResetAsync(db.Source);
        await using (var seed = new VeritasAtlasDbContext(Options(db.Source)))
        {
            var store = new EfTenantAuditEventStore(seed, TimeProvider.System);
            foreach (var (tenant, key, entity) in new[]
                     {
                         (TenantA, "evt-1", "inv-1"), (TenantA, "evt-2", "inv-2"), (TenantB, "evt-1", "inv-9")
                     })
            {
                var result = await store.AppendAsync(
                    tenant, new AuditEventInput(key, "investigation.created", "investigation", entity, "analyst-7"),
                    CancellationToken.None);
                Assert.AreEqual(AuditEventAppendOutcome.Appended, result.Outcome);
            }
        }

        var dump = Path.Combine(Path.GetTempPath(), $"veritas-drill-{Guid.NewGuid():N}.dump");
        try
        {
            Run("pg_dump", db.SourceBuilder, "--format=custom", "--no-owner", "--file", dump, db.SourceBuilder.Database!);

            await using (var admin = new NpgsqlConnection(db.Admin))
            {
                await admin.OpenAsync();
                await using var drop = admin.CreateCommand();
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{db.Restored.Database}\" WITH (FORCE)";
                await drop.ExecuteNonQueryAsync();
                await using var create = admin.CreateCommand();
                create.CommandText = $"CREATE DATABASE \"{db.Restored.Database}\"";
                await create.ExecuteNonQueryAsync();
            }

            Run("pg_restore", db.Restored, "--no-owner", "--exit-on-error", "--dbname", db.Restored.Database!, dump);
        }
        finally
        {
            File.Delete(dump);
        }

        var restored = db.Restored.ConnectionString;
        Assert.AreEqual(
            await Scalar(db.Source, "SELECT string_agg(\"MigrationId\", ',' ORDER BY \"MigrationId\") FROM \"__EFMigrationsHistory\""),
            await Scalar(restored, "SELECT string_agg(\"MigrationId\", ',' ORDER BY \"MigrationId\") FROM \"__EFMigrationsHistory\""),
            "migration history must survive the restore");

        const string checksum =
            "SELECT md5(string_agg(concat_ws('|', tenant_id, id, event_key, action, entity_type, entity_id, actor_subject, occurred_at_utc), ';' ORDER BY tenant_id, id)) FROM tenant_audit_events";
        Assert.AreEqual("3", await Scalar(restored, "SELECT count(*) FROM tenant_audit_events"));
        Assert.AreEqual(await Scalar(db.Source, checksum), await Scalar(restored, checksum), "audit rows must be byte-identical");

        await using var after = new VeritasAtlasDbContext(Options(restored));
        Assert.AreEqual(0, (await after.Database.GetPendingMigrationsAsync()).Count(), "a restore must not need re-migration");

        var restoredStore = new EfTenantAuditEventStore(after, TimeProvider.System);
        Assert.AreEqual(2, (await restoredStore.ListAsync(TenantA, null, null, CancellationToken.None)).Count);
        Assert.AreEqual(1, (await restoredStore.ListAsync(TenantB, null, null, CancellationToken.None)).Count);

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_audit_events SET actor_subject = 'someone-else'",
                     "DELETE FROM tenant_audit_events",
                     "TRUNCATE tenant_audit_events"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => after.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, $"restored ledger must stay append-only: {mutation}");
        }

        Assert.AreEqual("3", await Scalar(restored, "SELECT count(*) FROM tenant_audit_events"));
    }

    private static (string Source, NpgsqlConnectionStringBuilder SourceBuilder, NpgsqlConnectionStringBuilder Restored, string Admin)?
        Databases()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        if (!OnPath("pg_dump") || !OnPath("pg_restore"))
        {
            Assert.Inconclusive("pg_dump and pg_restore are not on PATH.");
            return null;
        }

        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
        var source = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        source.Database += "_drill_src";
        var restored = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        restored.Database += "_drill_restored";
        return (source.ConnectionString, source, restored, admin.ConnectionString);
    }

    private static bool OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, tool)));

    private static void Run(string tool, NpgsqlConnectionStringBuilder connection, params string[] args)
    {
        var info = new ProcessStartInfo(tool)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("--host");
        info.ArgumentList.Add(connection.Host!);
        info.ArgumentList.Add("--port");
        info.ArgumentList.Add(connection.Port.ToString());
        info.ArgumentList.Add("--username");
        info.ArgumentList.Add(connection.Username!);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["PGPASSWORD"] = connection.Password ?? "";

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, $"{tool} failed: {stderr.Result}");
    }

    private static async Task<string?> Scalar(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
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
