using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-03 slice 5 (#93): immutable tenant-scoped investigation minimum
/// schema. Database-backed tests run against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_investigations</c> database)
/// and are inconclusive otherwise; input validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantInvestigationStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var store = new EfTenantInvestigationStore(null!, TimeProvider.System);
        var cases = new (Guid Tenant, InvestigationInput Input)[]
        {
            (Guid.Empty, Input()),
            (TenantA, Input() with { CreationKey = "" }),
            (TenantA, Input() with { CreationKey = " " }),
            (TenantA, Input() with { CreationKey = new string('k', TenantInvestigationLimits.MaxCreationKeyLength + 1) }),
            (TenantA, Input() with { Title = "" }),
            (TenantA, Input() with { Title = "   " }),
            (TenantA, Input() with { Title = " Leading space" }),
            (TenantA, Input() with { Title = "Trailing space " }),
            (TenantA, Input() with { Title = "Trailing nbsp " }),
            (TenantA, Input() with { Title = "Line\nbreak" }),
            (TenantA, Input() with { Title = "Tab\tinside" }),
            (TenantA, Input() with { Title = "Del\u007f" }),
            (TenantA, Input() with { Title = "C1\u0085control" }),
            (TenantA, Input() with { Title = new string('t', TenantInvestigationLimits.MaxTitleLength + 1) }),
            (TenantA, Input() with { CreatedBySubject = "" }),
            (TenantA, Input() with { CreatedBySubject = "  " }),
            (TenantA, Input() with { CreatedBySubject = new string('s', TenantInvestigationLimits.MaxSubjectLength + 1) }),
            // The audit actor rule (#97): no C0, DEL or C1 control characters.
            (TenantA, Input() with { CreatedBySubject = "analyst\n7" }),
            (TenantA, Input() with { CreatedBySubject = "analyst\u007f" }),
            (TenantA, Input() with { CreatedBySubject = "analyst\u0085" }),
        };
        foreach (var (tenant, input) in cases)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                store.CreateAsync(tenant, input, CancellationToken.None), input.ToString());
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.GetAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.ListAsync(Guid.Empty, CancellationToken.None));
    }

    [TestMethod]
    public async Task CreatesReadsAndListsOnlyTheCallerTenantsInvestigations()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var store = new EfTenantInvestigationStore(db, TimeProvider.System);

        var first = await store.CreateAsync(TenantA, Input(), CancellationToken.None);
        Assert.AreEqual(InvestigationCreateOutcome.Created, first.Outcome);
        Assert.AreEqual("Procurement contracts 2025 — Région Est", first.Investigation!.Title);
        Assert.AreEqual("analyst-7", first.Investigation.CreatedBySubject);
        Assert.AreEqual(DateTimeKind.Utc, first.Investigation.CreatedAtUtc.Kind);

        var second = await store.CreateAsync(
            TenantA, Input() with { CreationKey = "inv-2", Title = "Second" }, CancellationToken.None);
        var other = await store.CreateAsync(TenantB, Input(), CancellationToken.None);
        Assert.AreEqual(InvestigationCreateOutcome.Created, other.Outcome);
        Assert.AreNotEqual(first.Investigation.Id, other.Investigation!.Id);

        Assert.AreEqual(first.Investigation, await store.GetAsync(TenantA, first.Investigation.Id, CancellationToken.None));
        // Another tenant's id is indistinguishable from an unknown one.
        Assert.IsNull(await store.GetAsync(TenantA, other.Investigation.Id, CancellationToken.None));
        Assert.IsNull(await store.GetAsync(TenantB, first.Investigation.Id, CancellationToken.None));
        Assert.IsNull(await store.GetAsync(TenantA, Guid.NewGuid(), CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { first.Investigation, second.Investigation },
            (await store.ListAsync(TenantA, CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(
            new[] { other.Investigation },
            (await store.ListAsync(TenantB, CancellationToken.None)).ToArray());
    }

    [TestMethod]
    public async Task CreationKeyReplaysOnlyTheSameInvestigation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var store = new EfTenantInvestigationStore(db, TimeProvider.System);

        var first = await store.CreateAsync(TenantA, Input(), CancellationToken.None);
        var replay = await store.CreateAsync(TenantA, Input(), CancellationToken.None);
        Assert.AreEqual(InvestigationCreateOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Investigation, replay.Investigation);

        foreach (var conflict in new[]
                 {
                     Input() with { Title = "Another title" },
                     Input() with { CreatedBySubject = "analyst-8" },
                 })
        {
            Assert.AreEqual(
                new InvestigationCreateResult(InvestigationCreateOutcome.IdempotencyConflict, null),
                await store.CreateAsync(TenantA, conflict, CancellationToken.None), conflict.ToString());
        }

        Assert.AreEqual(1, await db.TenantInvestigations.CountAsync());
        // Neither the replay nor the conflicts wrote an audit event (#97).
        Assert.AreEqual(1, await db.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task CreationWritesExactlyOneAuditEventInTheCallerTenant()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var store = new EfTenantInvestigationStore(db, TimeProvider.System);
        var audit = new EfTenantAuditEventStore(db, TimeProvider.System);

        var created = (await store.CreateAsync(TenantA, Input(), CancellationToken.None)).Investigation!;
        var id = created.Id.ToString("D");

        var events = await audit.ListAsync(TenantA, null, null, CancellationToken.None);
        Assert.AreEqual(1, events.Count);
        var evt = events[0];
        Assert.AreEqual($"investigation.created:{id}", evt.EventKey);
        Assert.AreEqual("investigation.created", evt.Action);
        Assert.AreEqual("investigation", evt.EntityType);
        Assert.AreEqual(id, evt.EntityId);
        Assert.AreEqual("analyst-7", evt.ActorSubject);
        Assert.AreEqual(created.CreatedAtUtc, evt.OccurredAtUtc);
        Assert.AreEqual(DateTimeKind.Utc, evt.OccurredAtUtc.Kind);
        Assert.AreEqual(1, (await audit.ListAsync(TenantA, "investigation", id, CancellationToken.None)).Count);

        // The event is tenant-scoped like the investigation itself.
        Assert.AreEqual(0, (await audit.ListAsync(TenantB, null, null, CancellationToken.None)).Count);
        await store.CreateAsync(TenantB, Input(), CancellationToken.None);
        Assert.AreEqual(1, (await audit.ListAsync(TenantB, null, null, CancellationToken.None)).Count);
        Assert.AreEqual(1, (await audit.ListAsync(TenantA, null, null, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task FailedAuditWriteRollsBackTheInvestigation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        // Test-only fault in this per-test database: every audit insert fails.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION test_refuse_audit() RETURNS trigger LANGUAGE plpgsql AS "
            + "$$BEGIN RAISE EXCEPTION 'audit unavailable' USING ERRCODE = 'P0001'; END$$");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER test_refuse_audit BEFORE INSERT ON tenant_audit_events "
            + "FOR EACH ROW EXECUTE FUNCTION test_refuse_audit()");

        var store = new EfTenantInvestigationStore(db, TimeProvider.System);
        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
            store.CreateAsync(TenantA, Input(), CancellationToken.None));
        Assert.AreEqual("P0001", (error.InnerException as PostgresException)?.SqlState);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(0, await check.TenantInvestigations.CountAsync());
        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task ConcurrentSameKeyCreatesYieldOneRow()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantInvestigationStore(db, TimeProvider.System)
                .CreateAsync(TenantA, Input(), CancellationToken.None);
        }));

        Assert.AreEqual(1, results.Count(r => r.Outcome == InvestigationCreateOutcome.Created));
        Assert.IsTrue(results.All(r => r.Outcome is InvestigationCreateOutcome.Created or InvestigationCreateOutcome.Replayed));
        Assert.AreEqual(1, results.Select(r => r.Investigation!.Id).Distinct().Count());
        await using var check = new VeritasAtlasDbContext(options);
        Assert.AreEqual(1, await check.TenantInvestigations.CountAsync());
        Assert.AreEqual(1, await check.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task SchemaRefusesInvalidRowsAndAnyMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var now = new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

        Task Insert(Guid tenant, string key, string title, string subject) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_investigations (tenant_id, id, creation_key, title, created_by_subject, created_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {key}, {title}, {subject}, {now})");

        await Insert(TenantA, "ok", "Valid title", "analyst-7");

        var refused = new (Guid Tenant, string Key, string Title, string Subject)[]
        {
            (Guid.Empty, "k1", "Valid title", "analyst-7"),
            (TenantA, " ", "Valid title", "analyst-7"),
            (TenantA, "k2", "", "analyst-7"),
            (TenantA, "k3", "   ", "analyst-7"),
            (TenantA, "k4", " Leading", "analyst-7"),
            (TenantA, "k5", "Trailing ", "analyst-7"),
            (TenantA, "k6", "Line\nbreak", "analyst-7"),
            (TenantA, "k7", "Del\u007f", "analyst-7"),
            (TenantA, "k8", "C1\u0085control", "analyst-7"),
            (TenantA, "k9", "Valid title", " "),
            // The creation key is unique per tenant.
            (TenantA, "ok", "Valid title", "analyst-7"),
        };
        foreach (var row in refused)
        {
            await Assert.ThrowsExactlyAsync<PostgresException>(
                () => Insert(row.Tenant, row.Key, row.Title, row.Subject), row.ToString());
        }

        // The same creation key in another tenant is a distinct investigation.
        await Insert(TenantB, "ok", "Valid title", "analyst-7");

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_investigations SET title = 'Renamed'",
                     "UPDATE tenant_investigations SET tenant_id = 'b2222222-2222-4222-8222-222222222222'",
                     "DELETE FROM tenant_investigations",
                     "TRUNCATE tenant_investigations"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        Assert.AreEqual(2, await db.TenantInvestigations.CountAsync());
        Assert.AreEqual(2, await db.TenantInvestigations.CountAsync(x => x.Title == "Valid title"));
    }

    private static InvestigationInput Input() =>
        new("inv-1", "Procurement contracts 2025 — Région Est", "analyst-7");

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_investigations";
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
