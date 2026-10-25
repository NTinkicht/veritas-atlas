using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-03 slice 6 (#95): append-only tenant-scoped audit event ledger.
/// Database-backed tests run against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_audit_events</c> database)
/// and are inconclusive otherwise; input validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantAuditEventStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var store = new EfTenantAuditEventStore(null!, TimeProvider.System);
        var cases = new (Guid Tenant, AuditEventInput Input)[]
        {
            (Guid.Empty, Input()),
            (TenantA, Input() with { EventKey = "" }),
            (TenantA, Input() with { EventKey = " " }),
            (TenantA, Input() with { EventKey = new string('k', TenantAuditEventLimits.MaxEventKeyLength + 1) }),
            (TenantA, Input() with { Action = "" }),
            (TenantA, Input() with { Action = "Investigation.created" }),
            (TenantA, Input() with { Action = "investigation created" }),
            (TenantA, Input() with { Action = "investigation..created" }),
            (TenantA, Input() with { Action = ".created" }),
            (TenantA, Input() with { Action = "created." }),
            (TenantA, Input() with { Action = "1created" }),
            (TenantA, Input() with { Action = "créé" }),
            (TenantA, Input() with { Action = "a" + new string('b', TenantAuditEventLimits.MaxActionLength) }),
            (TenantA, Input() with { EntityType = "" }),
            (TenantA, Input() with { EntityType = "Investigation" }),
            (TenantA, Input() with { EntityType = "a" + new string('b', TenantAuditEventLimits.MaxEntityTypeLength) }),
            (TenantA, Input() with { EntityId = "" }),
            (TenantA, Input() with { EntityId = "   " }),
            (TenantA, Input() with { EntityId = "id\nline" }),
            (TenantA, Input() with { EntityId = "C1\u0085" }),
            (TenantA, Input() with { EntityId = new string('i', TenantAuditEventLimits.MaxEntityIdLength + 1) }),
            (TenantA, Input() with { ActorSubject = "" }),
            (TenantA, Input() with { ActorSubject = "  " }),
            (TenantA, Input() with { ActorSubject = "tab\tsubject" }),
            (TenantA, Input() with { ActorSubject = new string('s', TenantAuditEventLimits.MaxSubjectLength + 1) }),
        };
        foreach (var (tenant, input) in cases)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                store.AppendAsync(tenant, input, CancellationToken.None), input.ToString());
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.ListAsync(Guid.Empty, null, null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.ListAsync(TenantA, null, "inv-1", CancellationToken.None));
    }

    [TestMethod]
    public async Task AppendsAndListsOnlyTheCallerTenantsEvents()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var clock = new SteppingTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        var store = new EfTenantAuditEventStore(db, clock);

        var first = await store.AppendAsync(TenantA, Input(), CancellationToken.None);
        Assert.AreEqual(AuditEventAppendOutcome.Appended, first.Outcome);
        Assert.AreEqual(
            new TenantAuditEventModel(
                first.Event!.Id, "evt-1", "investigation.created", "investigation", "inv-1", "analyst-7",
                new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc)),
            first.Event);
        Assert.AreEqual(DateTimeKind.Utc, first.Event.OccurredAtUtc.Kind);

        var second = await store.AppendAsync(
            TenantA, Input() with { EventKey = "evt-2", EntityId = "inv-2" }, CancellationToken.None);
        var third = await store.AppendAsync(
            TenantA,
            Input() with { EventKey = "evt-3", Action = "source.registered", EntityType = "source", EntityId = "src-1" },
            CancellationToken.None);
        var other = await store.AppendAsync(TenantB, Input(), CancellationToken.None);
        Assert.AreEqual(AuditEventAppendOutcome.Appended, other.Outcome);
        Assert.AreNotEqual(first.Event.Id, other.Event!.Id);

        CollectionAssert.AreEqual(
            new[] { first.Event, second.Event, third.Event },
            (await store.ListAsync(TenantA, null, null, CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(
            new[] { first.Event, second.Event },
            (await store.ListAsync(TenantA, "investigation", null, CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(
            new[] { second.Event },
            (await store.ListAsync(TenantA, "investigation", "inv-2", CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(
            new[] { other.Event },
            (await store.ListAsync(TenantB, null, null, CancellationToken.None)).ToArray());
        // Another tenant's entity is invisible, not an error.
        Assert.AreEqual(0, (await store.ListAsync(TenantB, "source", "src-1", CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task EventKeyReplaysOnlyTheSameEvent()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var store = new EfTenantAuditEventStore(db, TimeProvider.System);

        var first = await store.AppendAsync(TenantA, Input(), CancellationToken.None);
        var replay = await store.AppendAsync(TenantA, Input(), CancellationToken.None);
        Assert.AreEqual(AuditEventAppendOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Event, replay.Event);

        foreach (var conflict in new[]
                 {
                     Input() with { Action = "investigation.renamed" },
                     Input() with { EntityType = "source" },
                     Input() with { EntityId = "inv-9" },
                     Input() with { ActorSubject = "analyst-8" },
                 })
        {
            Assert.AreEqual(
                new AuditEventAppendResult(AuditEventAppendOutcome.IdempotencyConflict, null),
                await store.AppendAsync(TenantA, conflict, CancellationToken.None), conflict.ToString());
        }

        Assert.AreEqual(1, await db.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task ConcurrentSameKeyAppendsYieldOneRow()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantAuditEventStore(db, TimeProvider.System)
                .AppendAsync(TenantA, Input(), CancellationToken.None);
        }));

        Assert.AreEqual(1, results.Count(r => r.Outcome == AuditEventAppendOutcome.Appended));
        Assert.IsTrue(results.All(r => r.Outcome is AuditEventAppendOutcome.Appended or AuditEventAppendOutcome.Replayed));
        Assert.AreEqual(1, results.Select(r => r.Event!.Id).Distinct().Count());
        await using var check = new VeritasAtlasDbContext(options);
        Assert.AreEqual(1, await check.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task SchemaRefusesInvalidRowsAndAnyMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var now = new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

        Task Insert(Guid tenant, string key, string action, string entityType, string entityId, string subject) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_audit_events (tenant_id, id, event_key, action, entity_type, entity_id, actor_subject, occurred_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {key}, {action}, {entityType}, {entityId}, {subject}, {now})");

        await Insert(TenantA, "ok", "investigation.created", "investigation", "inv-1", "analyst-7");

        var refused = new (Guid Tenant, string Key, string Action, string EntityType, string EntityId, string Subject)[]
        {
            (Guid.Empty, "k1", "investigation.created", "investigation", "inv-1", "analyst-7"),
            (TenantA, " ", "investigation.created", "investigation", "inv-1", "analyst-7"),
            (TenantA, "k2", "", "investigation", "inv-1", "analyst-7"),
            (TenantA, "k3", "Investigation.Created", "investigation", "inv-1", "analyst-7"),
            (TenantA, "k4", "free text action", "investigation", "inv-1", "analyst-7"),
            (TenantA, "k5", "investigation..created", "investigation", "inv-1", "analyst-7"),
            (TenantA, "k6", "investigation.created", "Investigation", "inv-1", "analyst-7"),
            (TenantA, "k7", "investigation.created", "investigation", " ", "analyst-7"),
            (TenantA, "k8", "investigation.created", "investigation", "inv\n1", "analyst-7"),
            (TenantA, "k9", "investigation.created", "investigation", "inv-1", ""),
            (TenantA, "k10", "investigation.created", "investigation", "inv-1", "C1\u0085"),
            // The event key is unique per tenant.
            (TenantA, "ok", "investigation.created", "investigation", "inv-1", "analyst-7"),
        };
        foreach (var row in refused)
        {
            await Assert.ThrowsExactlyAsync<PostgresException>(
                () => Insert(row.Tenant, row.Key, row.Action, row.EntityType, row.EntityId, row.Subject),
                row.ToString());
        }

        // The same event key in another tenant is a distinct event.
        await Insert(TenantB, "ok", "investigation.created", "investigation", "inv-1", "analyst-7");

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_audit_events SET actor_subject = 'someone-else'",
                     "UPDATE tenant_audit_events SET tenant_id = 'b2222222-2222-4222-8222-222222222222'",
                     "DELETE FROM tenant_audit_events",
                     "TRUNCATE tenant_audit_events"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        Assert.AreEqual(2, await db.TenantAuditEvents.CountAsync());
        Assert.AreEqual(2, await db.TenantAuditEvents.CountAsync(x => x.ActorSubject == "analyst-7"));
    }

    private static AuditEventInput Input() =>
        new("evt-1", "investigation.created", "investigation", "inv-1", "analyst-7");

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_audit_events";
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

    /// <summary>Each read advances one second, so append order is deterministic.</summary>
    private sealed class SteppingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _next = start;

        public override DateTimeOffset GetUtcNow()
        {
            var now = _next;
            _next = _next.AddSeconds(1);
            return now;
        }
    }
}
