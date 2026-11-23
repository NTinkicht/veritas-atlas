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
/// VER-P1-01 slice 1: private saved evidence searches. Refusals and body validation are proven before
/// the store is touched; create, list, delete, privacy, tenant isolation, cap and audit run against
/// real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_svsrch</c> database).
/// Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantSavedSearchTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private const string Path = "/api/v1/tenant-saved-searches";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Token(string? subject, string role, Guid tenant) =>
        TenantInvestigationEvidenceItemTests.Sign(subject, [role], [tenant.ToString("D")]);

    private static Task<HttpResponseMessage> Send(HttpClient c, HttpMethod m, string path, string bearer, object? body = null) =>
        TenantInvestigationEvidenceItemTests.Send(c, m, path, bearer, body);

    [TestMethod]
    public async Task RefusedCallersNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var a = TenantA.ToString("D");
        var body = new { name = "n", query = "q" };

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], ["not-a-uuid"]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "operator"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["analyst"], [a]),
                     Token("s", "reviewer", TenantA),
                     Token("s", "publisher", TenantA),
                     Token("s", "admin", TenantA),
                 })
        {
            foreach (var (method, path) in new[]
                     {
                         (HttpMethod.Post, Path), (HttpMethod.Get, Path), (HttpMethod.Delete, $"{Path}/{Guid.NewGuid()}"),
                         (HttpMethod.Put, $"{Path}/{Guid.NewGuid()}"), (HttpMethod.Get, $"{Path}/{Guid.NewGuid()}/versions")
                     })
            {
                using var response = await Send(client, method, path, bearer,
                    method == HttpMethod.Post ? body : method == HttpMethod.Put ? new { expectedVersion = 1, name = "n", query = "q" } : null);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
            }
        }

        using var anonymous = await client.GetAsync(Path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, store.Calls, "a refused caller must never reach the store");

        // Positive control: tenant and owner come from the token, never from the request.
        using var ok = await Send(client, HttpMethod.Post, $"{Path}?tenant={TenantB}&subject=spoofed",
            Token("synthetic-analyst", "analyst", TenantA), body);
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, "synthetic-analyst"), store.Created.Single());
    }

    [TestMethod]
    public async Task InvalidBodiesAreRefusedBeforeTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var bearer = Token("synthetic-analyst", "analyst", TenantA);

        var invalid = new object[]
        {
            new { name = "", query = "q" },
            new { name = " padded", query = "q" },
            new { name = new string('n', 101), query = "q" },
            new { name = "a\u0001b", query = "q" },
            new { name = "n", query = "" },
            new { name = "n", query = "   " },
            new { name = "n", query = new string('q', 257) },
            new { name = "n", query = "a\u0007b" },
            new { name = "n", query = "q", freshness = "Bogus" },
            new { name = "n", query = "q", freshness = "7" },
            new { name = "n", query = "q", freshness = "" },
            new { name = "n", query = "q", jurisdiction = "" },
            new { name = "n", query = "q", sourceType = new string('t', 129) },
            new { name = "n", query = "q", subjectTerm = "  " },
            new { name = "n", query = "q", parserName = "p\nq" },
            new { name = "n", query = "q", sourceEntryId = Guid.Empty },
            new { name = (string?)null, query = "q" },
        };
        foreach (var body in invalid)
        {
            using var response = await Send(client, HttpMethod.Post, Path, bearer, body);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, System.Text.Json.JsonSerializer.Serialize(body));
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_saved_search");
        }

        using var empty = await Send(client, HttpMethod.Post, Path, bearer);
        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.AreEqual(0, store.Calls, "an invalid body must never reach the store");

        // Positive control: a fully populated valid body passes and Freshness is stored canonically.
        var source = Guid.NewGuid();
        using var ok = await Send(client, HttpMethod.Post, Path, bearer, new
        {
            name = "synthetic", query = "q", sourceEntryId = source, jurisdiction = "UK", sourceType = "legislation",
            freshness = "current", parserName = "p", parserVersion = "1", subjectTerm = "s"
        });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual(1, store.Calls);
        Assert.AreEqual("Current", store.LastDefinition!.Freshness);
        Assert.AreEqual(source, store.LastDefinition.SourceEntryId);
    }

    [TestMethod]
    public async Task InvalidUpdateBodiesAreRefusedBeforeTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var bearer = Token("synthetic-analyst", "analyst", TenantA);
        var path = $"{Path}/{Guid.NewGuid()}";

        var invalid = new object[]
        {
            new { name = "n", query = "q" },
            new { expectedVersion = 0, name = "n", query = "q" },
            new { expectedVersion = -1, name = "n", query = "q" },
            new { expectedVersion = 1, name = " n", query = "q" },
            new { expectedVersion = 1, name = "n", query = "" },
            new { expectedVersion = 1, name = "n", query = "q", freshness = "Bogus" },
            new { expectedVersion = 1, name = "n", query = "q", sourceEntryId = Guid.Empty },
        };
        foreach (var body in invalid)
        {
            using var response = await Send(client, HttpMethod.Put, path, bearer, body);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, System.Text.Json.JsonSerializer.Serialize(body));
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_saved_search");
        }

        using var empty = await Send(client, HttpMethod.Put, path, bearer);
        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.AreEqual(0, store.Calls, "an invalid update must never reach the store");

        // Positive control: the same shape with a valid body reaches the store with the expected version.
        using var ok = await Send(client, HttpMethod.Put, path, bearer,
            new { expectedVersion = 3, name = "n", query = "q", freshness = "stale" });
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
        Assert.AreEqual(1, store.Calls);
        Assert.AreEqual(3, store.LastExpectedVersion);
        Assert.AreEqual("Stale", store.LastDefinition!.Freshness);
    }

    [TestMethod]
    public async Task SavedSearchVersionsAreOptimisticPrivateAndAuditedOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var bob = Token("synthetic-bob", "operator", TenantA);
        var aliceB = Token("synthetic-alice", "analyst", TenantB);

        using var created = await Send(client, HttpMethod.Post, Path, alice, new { name = "acts", query = "tax" });
        var first = (await created.Content.ReadFromJsonAsync<Saved>())!;
        Assert.AreEqual(1, first.Version);
        using var other = await Send(client, HttpMethod.Post, Path, alice, new { name = "other", query = "x" });
        Assert.AreEqual(HttpStatusCode.Created, other.StatusCode);

        // An edit appends version 2 and keeps version 1 readable.
        using var edited = await Send(client, HttpMethod.Put, $"{Path}/{first.Id}", alice,
            new { expectedVersion = 1, name = "acts", query = "vat", jurisdiction = "UK", freshness = "current" });
        Assert.AreEqual(HttpStatusCode.OK, edited.StatusCode);
        var v2 = (await edited.Content.ReadFromJsonAsync<Saved>())!;
        Assert.AreEqual(2, v2.Version);
        Assert.AreEqual("vat", v2.Query);
        Assert.AreEqual("Current", v2.Freshness);
        CollectionAssert.AreEqual(new[] { "x", "vat" }, (await ListQueries(client, alice)).OrderByDescending(q => q).ToArray());

        // Replaying the same edit is a stale expected version, not a silent second write.
        using var replay = await Send(client, HttpMethod.Put, $"{Path}/{first.Id}", alice,
            new { expectedVersion = 1, name = "acts", query = "vat", jurisdiction = "UK", freshness = "current" });
        Assert.AreEqual(HttpStatusCode.Conflict, replay.StatusCode);
        StringAssert.Contains(await replay.Content.ReadAsStringAsync(), "saved_search_version_conflict");

        // An identical definition at the current version changes nothing.
        using var same = await Send(client, HttpMethod.Put, $"{Path}/{first.Id}", alice,
            new { expectedVersion = 2, name = "acts", query = "vat", jurisdiction = "UK", freshness = "current" });
        Assert.AreEqual(HttpStatusCode.OK, same.StatusCode);
        Assert.AreEqual(2, (await same.Content.ReadFromJsonAsync<Saved>())!.Version);

        // A rename onto another saved search of the same owner is refused and writes no version.
        using var clash = await Send(client, HttpMethod.Put, $"{Path}/{first.Id}", alice,
            new { expectedVersion = 2, name = "other", query = "vat", jurisdiction = "UK", freshness = "current" });
        Assert.AreEqual(HttpStatusCode.Conflict, clash.StatusCode);
        StringAssert.Contains(await clash.Content.ReadAsStringAsync(), "saved_search_name_taken");

        using var history = await Send(client, HttpMethod.Get, $"{Path}/{first.Id}/versions", alice);
        Assert.AreEqual(HttpStatusCode.OK, history.StatusCode);
        var items = (await history.Content.ReadFromJsonAsync<VersionList>())!.Items;
        CollectionAssert.AreEqual(new[] { 1, 2 }, items.Select(i => i.Version).ToArray());
        CollectionAssert.AreEqual(new[] { "tax", "vat" }, items.Select(i => i.Query).ToArray());
        Assert.IsNull(items[0].Freshness);
        Assert.AreEqual("Current", items[1].Freshness);

        // Another subject or tenant cannot edit or read the history: identical 404 to an unknown id.
        var body = new { expectedVersion = 2, name = "stolen", query = "q" };
        using var unknownPut = await Send(client, HttpMethod.Put, $"{Path}/{Guid.NewGuid()}", bob, body);
        using var bobPut = await Send(client, HttpMethod.Put, $"{Path}/{first.Id}", bob, body);
        using var tenantPut = await Send(client, HttpMethod.Put, $"{Path}/{first.Id}", aliceB, body);
        using var unknownGet = await Send(client, HttpMethod.Get, $"{Path}/{Guid.NewGuid()}/versions", bob);
        using var bobGet = await Send(client, HttpMethod.Get, $"{Path}/{first.Id}/versions", bob);
        using var tenantGet = await Send(client, HttpMethod.Get, $"{Path}/{first.Id}/versions", aliceB);
        foreach (var r in new[] { unknownPut, bobPut, tenantPut, unknownGet, bobGet, tenantGet })
        {
            Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);
        }

        Assert.AreEqual(await unknownPut.Content.ReadAsStringAsync(), await bobPut.Content.ReadAsStringAsync());
        Assert.AreEqual(await unknownPut.Content.ReadAsStringAsync(), await tenantPut.Content.ReadAsStringAsync());
        Assert.AreEqual(await unknownGet.Content.ReadAsStringAsync(), await bobGet.Content.ReadAsStringAsync());
        Assert.AreEqual(await unknownGet.Content.ReadAsStringAsync(), await tenantGet.Content.ReadAsStringAsync());
        Assert.AreEqual("vat", (await ListQueries(client, alice)).First(q => q != "x"));

        // Only the two effective writes are audited; the version history is removed with the saved search.
        var actions = await db.TenantAuditEvents.AsNoTracking()
            .Where(x => x.TenantId == TenantA && x.EntityId == first.Id.ToString("D"))
            .Select(x => x.Action).ToListAsync();
        CollectionAssert.AreEquivalent(new[] { "saved_search.created", "saved_search.updated" }, actions);
        Assert.AreEqual(2, await db.TenantSavedSearchVersions.CountAsync(x => x.SavedSearchId == first.Id));
        using var removed = await Send(client, HttpMethod.Delete, $"{Path}/{first.Id}", alice);
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.AreEqual(0, await db.TenantSavedSearchVersions.CountAsync(x => x.SavedSearchId == first.Id));
    }

    [TestMethod]
    public async Task ConcurrentEditsOfOneVersionAdmitExactlyOneAndTheVersionCapHoldsOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var alice = Token("synthetic-alice", "analyst", TenantA);
        using var created = await Send(client, HttpMethod.Post, Path, alice, new { name = "acts", query = "q0" });
        var id = (await created.Content.ReadFromJsonAsync<Saved>())!.Id;

        var statuses = await Task.WhenAll(Enumerable.Range(1, 8).Select(async i =>
        {
            using var response = await Send(client, HttpMethod.Put, $"{Path}/{id}", alice,
                new { expectedVersion = 1, name = "acts", query = $"q{i}" });
            return response.StatusCode;
        }));
        Assert.AreEqual(1, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.AreEqual(7, statuses.Count(s => s == HttpStatusCode.Conflict));

        // Walk to the cap, then the next effective edit is refused for that reason.
        for (var v = 2; v < TenantSavedSearchLimits.MaxVersions; v++)
        {
            using var step = await Send(client, HttpMethod.Put, $"{Path}/{id}", alice,
                new { expectedVersion = v, name = "acts", query = $"step{v}" });
            Assert.AreEqual(HttpStatusCode.OK, step.StatusCode, $"v{v}");
        }

        using var over = await Send(client, HttpMethod.Put, $"{Path}/{id}", alice,
            new { expectedVersion = TenantSavedSearchLimits.MaxVersions, name = "acts", query = "over" });
        Assert.AreEqual(HttpStatusCode.Conflict, over.StatusCode);
        StringAssert.Contains(await over.Content.ReadAsStringAsync(), "saved_search_version_limit_reached");
        using var history = await Send(client, HttpMethod.Get, $"{Path}/{id}/versions", alice);
        Assert.AreEqual(TenantSavedSearchLimits.MaxVersions,
            (await history.Content.ReadFromJsonAsync<VersionList>())!.Items.Count);
    }

    [TestMethod]
    public async Task SavedSearchesArePrivateCappedAndAuditedOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var bob = Token("synthetic-bob", "operator", TenantA);
        var aliceB = Token("synthetic-alice", "analyst", TenantB);

        using var created = await Send(client, HttpMethod.Post, Path, alice, new
        {
            name = "uk acts", query = "tax", jurisdiction = "UK", freshness = "CURRENT", parserName = "uk", parserVersion = "1"
        });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var first = (await created.Content.ReadFromJsonAsync<Saved>())!;
        Assert.AreEqual("Current", first.Freshness);
        Assert.AreEqual("UK", first.Jurisdiction);

        // Same name again: refused; the same name for another subject or tenant is a different row.
        using var duplicate = await Send(client, HttpMethod.Post, Path, alice, new { name = "uk acts", query = "other" });
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);
        StringAssert.Contains(await duplicate.Content.ReadAsStringAsync(), "saved_search_name_taken");
        using var bobSame = await Send(client, HttpMethod.Post, Path, bob, new { name = "uk acts", query = "bob" });
        using var otherTenant = await Send(client, HttpMethod.Post, Path, aliceB, new { name = "uk acts", query = "b" });
        Assert.AreEqual(HttpStatusCode.Created, bobSame.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, otherTenant.StatusCode);

        // Each caller lists only their own rows.
        CollectionAssert.AreEqual(new[] { "tax" }, await ListQueries(client, alice));
        CollectionAssert.AreEqual(new[] { "bob" }, await ListQueries(client, bob));
        CollectionAssert.AreEqual(new[] { "b" }, await ListQueries(client, aliceB));

        // Another subject or tenant cannot delete it and learns nothing: identical 404 to an unknown id.
        using var unknown = await Send(client, HttpMethod.Delete, $"{Path}/{Guid.NewGuid()}", bob);
        using var foreignSubject = await Send(client, HttpMethod.Delete, $"{Path}/{first.Id}", bob);
        using var foreignTenant = await Send(client, HttpMethod.Delete, $"{Path}/{first.Id}", aliceB);
        Assert.AreEqual(HttpStatusCode.NotFound, foreignSubject.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, foreignTenant.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.AreEqual(unknownBody, await foreignSubject.Content.ReadAsStringAsync());
        Assert.AreEqual(unknownBody, await foreignTenant.Content.ReadAsStringAsync());
        Assert.AreEqual(1, (await ListQueries(client, alice)).Length);

        using var removed = await Send(client, HttpMethod.Delete, $"{Path}/{first.Id}", alice);
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        using var again = await Send(client, HttpMethod.Delete, $"{Path}/{first.Id}", alice);
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode);
        Assert.AreEqual(0, (await ListQueries(client, alice)).Length);

        // One audit event per effective change, attributed to the owner; refusals wrote none.
        var actions = await db.TenantAuditEvents.AsNoTracking()
            .Where(x => x.TenantId == TenantA && x.EntityType == EfTenantSavedSearchStore.AuditEntityType)
            .Select(x => x.Action + "|" + x.ActorSubject).ToListAsync();
        CollectionAssert.AreEquivalent(
            new[]
            {
                "saved_search.created|synthetic-alice", "saved_search.created|synthetic-bob", "saved_search.deleted|synthetic-alice"
            },
            actions);

        // The database itself refuses an unknown freshness value.
        var ex = await Assert.ThrowsExactlyAsync<DbUpdateException>(async () =>
        {
            db.TenantSavedSearches.Add(new TenantSavedSearch
            {
                TenantId = TenantA, Id = Guid.NewGuid(), Subject = "x", Name = "bad", Query = "q",
                Freshness = "current", CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
        Assert.AreEqual("23514", (ex.InnerException as PostgresException)?.SqlState);
    }

    [TestMethod]
    public async Task ConcurrentCreatesCannotExceedThePerSubjectCapOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var bob = Token("synthetic-bob", "analyst", TenantA);

        var statuses = await Task.WhenAll(Enumerable.Range(0, TenantSavedSearchLimits.MaxPerSubject + 8).Select(async i =>
        {
            using var response = await Send(client, HttpMethod.Post, Path, alice, new { name = $"s{i}", query = "q" });
            return response.StatusCode;
        }));
        Assert.AreEqual(TenantSavedSearchLimits.MaxPerSubject, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.AreEqual(8, statuses.Count(s => s == HttpStatusCode.Conflict));
        Assert.AreEqual(TenantSavedSearchLimits.MaxPerSubject, (await ListQueries(client, alice)).Length);

        using var over = await Send(client, HttpMethod.Post, Path, alice, new { name = "extra", query = "q" });
        StringAssert.Contains(await over.Content.ReadAsStringAsync(), "saved_search_limit_reached");

        // The cap is per subject: another subject is unaffected.
        using var other = await Send(client, HttpMethod.Post, Path, bob, new { name = "extra", query = "q" });
        Assert.AreEqual(HttpStatusCode.Created, other.StatusCode);
    }

    private static async Task<string[]> ListQueries(HttpClient client, string bearer)
    {
        using var response = await Send(client, HttpMethod.Get, Path, bearer);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SavedList>())!.Items.Select(i => i.Query).ToArray();
    }

    private sealed record Saved(Guid Id, string Name, string Query, string? Jurisdiction, string? Freshness, int Version);

    private sealed record VersionRow(int Version, string Query, string? Freshness);

    private sealed record VersionList(List<VersionRow> Items);

    private sealed record SavedList(List<Saved> Items);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_svsrch";
        return builder.ConnectionString;
    }

    /// <summary>Store double that counts calls and records the accepted caller and definition.</summary>
    private sealed class CountingStore : ITenantSavedSearchStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, string Subject)> Created { get; } = [];
        public TenantSavedSearchDefinition? LastDefinition { get; private set; }
        public int LastExpectedVersion { get; private set; }

        public Task<SavedSearchCreateResult> CreateAsync(
            Guid tenantId, string subject, TenantSavedSearchDefinition definition, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Created.Add((tenantId, subject));
            LastDefinition = definition;
            return Task.FromResult(new SavedSearchCreateResult(
                SavedSearchCreateOutcome.Created,
                new TenantSavedSearchModel(Guid.NewGuid(), definition, DateTime.UtcNow)));
        }

        public Task<IReadOnlyList<TenantSavedSearchModel>> ListAsync(Guid tenantId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantSavedSearchModel>>([]);
        }

        public Task<SavedSearchUpdateResult> UpdateAsync(
            Guid tenantId, string subject, Guid id, int expectedVersion, TenantSavedSearchDefinition definition,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastDefinition = definition;
            LastExpectedVersion = expectedVersion;
            return Task.FromResult(new SavedSearchUpdateResult(
                SavedSearchUpdateOutcome.Updated,
                new TenantSavedSearchModel(id, definition, DateTime.UtcNow, expectedVersion + 1)));
        }

        public Task<IReadOnlyList<TenantSavedSearchVersionModel>?> ListVersionsAsync(
            Guid tenantId, string subject, Guid id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantSavedSearchVersionModel>?>([]);
        }

        public Task<bool> DeleteAsync(Guid tenantId, string subject, Guid id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(true);
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantSavedSearchStore? store = null)
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
