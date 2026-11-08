using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-10 slice 5: private per-subject bookmarks on collected evidence items. Refusals are proven
/// before the store is touched; put, replay, removal, audit, privacy, same-tenant guard and
/// cross-tenant denial run against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own
/// <c>*_bkmk</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationBookmarkTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Mark(Guid investigation, Guid item) =>
        $"/api/v1/tenant-investigations/{investigation}/evidence-items/{item}/bookmark";

    private static string Listing(Guid investigation) => $"/api/v1/tenant-investigations/{investigation}/bookmarks";

    private static string Token(string? subject, string role, Guid tenant) =>
        TenantInvestigationEvidenceItemTests.Sign(subject, [role], [tenant.ToString("D")]);

    [TestMethod]
    public async Task RefusedCallersNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var inv = Guid.NewGuid();
        var item = Guid.NewGuid();
        var a = TenantA.ToString("D");

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], ["not-a-uuid"]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["analyst"], [a]),
                     Token("s", "reviewer", TenantA),
                     Token("s", "admin", TenantA),
                 })
        {
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
            {
                using var response = await TenantInvestigationEvidenceItemTests.Send(client, method, Mark(inv, item), bearer);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
            }
        }

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["anonymous"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "reviewer"], [a]),
                 })
        {
            using var response = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Get, Listing(inv), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var anonymous = await client.PutAsync(Mark(inv, item), null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, store.Calls, "a refused caller must never reach the store");

        using var ok = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Put, $"{Mark(inv, item)}?tenant={TenantB}&subject=spoofed",
            Token("synthetic-analyst", "analyst", TenantA));
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, item, "synthetic-analyst"), store.Put.Single());
    }

    [TestMethod]
    public async Task PutReplayRemoveAuditPrivacyAndCrossTenantDenialOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var verificationA = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var analystA = Token("synthetic-analyst-a", "analyst", TenantA);
        var operatorA = Token("synthetic-operator-a", "operator", TenantA);
        var reviewerA = Token("synthetic-reviewer-a", "reviewer", TenantA);
        var analystB = Token("synthetic-analyst-b", "analyst", TenantB);
        var invA = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analystA);
        var invA2 = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analystA, "k2");
        var invB = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analystB);
        var itemA = (await new EfTenantInvestigationEvidenceStore(db, TimeProvider.System).CollectAsync(
            TenantA, invA, new EvidenceItemInput(verificationA, null, "synthetic"), CancellationToken.None)).Item!.Id;

        using var created = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Put, Mark(invA, itemA), analystA);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var first = (await created.Content.ReadFromJsonAsync<Bookmark>())!;
        Assert.AreEqual((invA, itemA), (first.InvestigationId, first.EvidenceItemId));
        using var replay = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Put, Mark(invA, itemA), analystA);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(first, await replay.Content.ReadFromJsonAsync<Bookmark>());
        using var other = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Put, Mark(invA, itemA), operatorA);
        Assert.AreEqual(HttpStatusCode.Created, other.StatusCode);

        // Another tenant's ids, a mismatched investigation and unknown ids answer identically.
        using var unknown = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Put, Mark(invA, Guid.NewGuid()), analystA);
        foreach (var (method, path, bearer) in new[]
                 {
                     (HttpMethod.Put, Mark(invB, itemA), analystA), (HttpMethod.Put, Mark(invA, itemA), analystB),
                     (HttpMethod.Put, Mark(invA2, itemA), analystA), (HttpMethod.Delete, Mark(invA, itemA), analystB),
                     (HttpMethod.Delete, Mark(invA2, itemA), analystA), (HttpMethod.Get, Listing(invB), analystA),
                     (HttpMethod.Get, Listing(Guid.NewGuid()), analystA),
                 })
        {
            using var response = await TenantInvestigationEvidenceItemTests.Send(client, method, path, bearer);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
            if (method != HttpMethod.Get)
            {
                Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
            }
        }

        // Bookmarks are private to their subject.
        using var mine = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Get, Listing(invA), analystA);
        var mineItems = (await mine.Content.ReadFromJsonAsync<BookmarkList>())!.Items;
        Assert.AreEqual(itemA, mineItems.Single().EvidenceItemId);
        using var reviewerView = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Get, Listing(invA), reviewerA);
        Assert.AreEqual(HttpStatusCode.OK, reviewerView.StatusCode);
        Assert.AreEqual(0, (await reviewerView.Content.ReadFromJsonAsync<BookmarkList>())!.Items.Count);

        // Removal deletes only the caller's own row and is idempotent.
        using var removed = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Delete, Mark(invA, itemA), analystA);
        using var removedAgain = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Delete, Mark(invA, itemA), analystA);
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, removedAgain.StatusCode);
        var remaining = await db.TenantInvestigationBookmarks.AsNoTracking().ToListAsync();
        Assert.AreEqual("synthetic-operator-a", remaining.Single().Subject);
        Assert.AreEqual(1, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync(), "the evidence item is never touched");

        // Exactly one audit event per effective add or removal (replay, no-op and refusals write none).
        var audits = await db.TenantAuditEvents.AsNoTracking()
            .Where(e => e.Action.StartsWith("investigation.bookmark_")).ToListAsync();
        Assert.AreEqual(2, audits.Count(e => e.Action == "investigation.bookmark_added"));
        Assert.AreEqual(1, audits.Count(e => e.Action == "investigation.bookmark_removed"));
        Assert.IsTrue(audits.All(e => e.TenantId == TenantA && e.EntityId == itemA.ToString("D")));
    }

    [TestMethod]
    public async Task ConcurrentIdenticalPutsStoreExactlyOneRowOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var verification = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var analyst = Token("synthetic-analyst-a", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analyst);
        var item = (await new EfTenantInvestigationEvidenceStore(db, TimeProvider.System).CollectAsync(
            TenantA, inv, new EvidenceItemInput(verification, null, "synthetic"), CancellationToken.None)).Item!.Id;

        var statuses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Put, Mark(inv, item), analyst);
            return response.StatusCode;
        }));

        Assert.IsTrue(statuses.All(s => s is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.AreEqual(1, await db.TenantInvestigationBookmarks.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "investigation.bookmark_added"));
    }

    [TestMethod]
    public async Task DatabaseRefusesCrossTenantAndRepointedBookmarksOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var verificationA = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var investigations = new EfTenantInvestigationStore(db, TimeProvider.System);
        var invA = (await investigations.CreateAsync(TenantA, new InvestigationInput("k", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var invA2 = (await investigations.CreateAsync(TenantA, new InvestigationInput("k2", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var item = (await new EfTenantInvestigationEvidenceStore(db, TimeProvider.System).CollectAsync(
            TenantA, invA, new EvidenceItemInput(verificationA, null, "synthetic"), CancellationToken.None)).Item!.Id;
        var store = new EfTenantInvestigationBookmarkStore(db, TimeProvider.System);
        Assert.AreEqual(BookmarkPutOutcome.Added, (await store.PutAsync(TenantA, invA, item, "synthetic", CancellationToken.None)).Outcome);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        foreach (var (tenant, investigation, evidenceItem) in new[]
                 {
                     (TenantB, invA, item), (TenantA, invA2, item), (TenantA, invA, Guid.NewGuid()),
                 })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_bookmarks (tenant_id, evidence_item_id, subject, investigation_id, created_at_utc) VALUES (@t, @e, 'other', @i, now())",
                connection);
            insert.Parameters.AddWithValue("t", tenant);
            insert.Parameters.AddWithValue("e", evidenceItem);
            insert.Parameters.AddWithValue("i", investigation);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState);
        }

        await using var update = new NpgsqlCommand("UPDATE tenant_investigation_bookmarks SET investigation_id = @i", connection);
        update.Parameters.AddWithValue("i", invA2);
        var updateError = await Assert.ThrowsExactlyAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
        Assert.AreEqual("55000", updateError.SqlState);

        Assert.AreEqual(1, await db.TenantInvestigationBookmarks.AsNoTracking().CountAsync());
        Assert.AreEqual(BookmarkPutOutcome.NotFound, (await store.PutAsync(TenantB, invA, item, "synthetic", CancellationToken.None)).Outcome);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.PutAsync(TenantA, invA, item, " ", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.PutAsync(Guid.Empty, invA, item, "synthetic", CancellationToken.None));
    }

    private sealed record Bookmark(Guid InvestigationId, Guid EvidenceItemId, DateTime CreatedAtUtc);

    private sealed record BookmarkList(List<Bookmark> Items);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_bkmk";
        return builder.ConnectionString;
    }

    /// <summary>Store double that counts calls and records accepted bookmarks.</summary>
    private sealed class CountingStore : ITenantInvestigationBookmarkStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, Guid Item, string Subject)> Put { get; } = [];

        Task<BookmarkPutResult> ITenantInvestigationBookmarkStore.PutAsync(
            Guid tenantId, Guid investigationId, Guid evidenceItemId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Put.Add((tenantId, evidenceItemId, subject));
            return Task.FromResult(new BookmarkPutResult(
                BookmarkPutOutcome.Added,
                new TenantInvestigationBookmarkModel(investigationId, evidenceItemId, subject, DateTime.UtcNow)));
        }

        public Task<BookmarkRemoveOutcome> RemoveAsync(
            Guid tenantId, Guid investigationId, Guid evidenceItemId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(BookmarkRemoveOutcome.Removed);
        }

        public Task<IReadOnlyList<TenantInvestigationBookmarkModel>?> ListAsync(
            Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantInvestigationBookmarkModel>?>(null);
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantInvestigationBookmarkStore? store = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0006");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                connectionString ?? "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                if (store is not null) services.AddSingleton(store);
            });
        }
    }
}
