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
/// VER-P1-01 slice 2: private per-subject investigation tags. Refusals and tag validation are proven
/// before the store is touched; add, replay, removal, audit, privacy, cap, closed-investigation
/// refusal, tenant isolation and the database guards run against real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_tags</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationTagTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Tags(Guid investigation) => $"/api/v1/tenant-investigations/{investigation}/tags";

    private static string Tag(Guid investigation, string tag) => $"{Tags(investigation)}/{tag}";

    private static string Token(string? subject, string role, Guid tenant) =>
        TenantInvestigationEvidenceItemTests.Sign(subject, [role], [tenant.ToString("D")]);

    private static Task<HttpResponseMessage> Send(HttpClient c, HttpMethod m, string path, string bearer, object? body = null) =>
        TenantInvestigationEvidenceItemTests.Send(c, m, path, bearer, body);

    [TestMethod]
    public async Task RefusedCallersNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store, shares: new FixedShares(true));
        using var client = factory.CreateClient();
        var a = TenantA.ToString("D");
        var inv = Guid.NewGuid();

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], ["not-a-uuid"]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "operator"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["intruder"], [a]),
                 })
        {
            foreach (var (method, path) in new[]
                     {
                         (HttpMethod.Put, Tag(inv, "t")), (HttpMethod.Delete, Tag(inv, "t")), (HttpMethod.Get, Tags(inv))
                     })
            {
                using var response = await Send(client, method, path, bearer);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
            }
        }

        // Read-only roles may list but never write.
        foreach (var role in new[] { "reviewer", "publisher", "admin" })
        {
            using var put = await Send(client, HttpMethod.Put, Tag(inv, "t"), Token("s", role, TenantA));
            using var delete = await Send(client, HttpMethod.Delete, Tag(inv, "t"), Token("s", role, TenantA));
            Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode, role);
            Assert.AreEqual(HttpStatusCode.Forbidden, delete.StatusCode, role);
        }

        using var anonymous = await client.GetAsync(Tags(inv));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, store.Calls, "a refused caller must never reach the store");

        // Positive control: tenant and owner come from the token, never from the request.
        using var ok = await Send(client, HttpMethod.Put, $"{Tag(inv, "t")}?tenant={TenantB}&subject=spoofed",
            Token("synthetic-analyst", "analyst", TenantA));
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, "synthetic-analyst"), store.Added.Single());
        using var reviewerList = await Send(client, HttpMethod.Get, Tags(inv), Token("synthetic-reviewer", "reviewer", TenantA));
        Assert.AreEqual(HttpStatusCode.OK, reviewerList.StatusCode);
    }

    [TestMethod]
    public async Task InvalidTagsAreRefusedBeforeTheStoreAndCaseIsFolded()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store, shares: new FixedShares(true));
        using var client = factory.CreateClient();
        var bearer = Token("synthetic-analyst", "analyst", TenantA);
        var inv = Guid.NewGuid();

        foreach (var bad in new[]
                 {
                     "_leading", "-leading", ".leading", "has%20space", "semi;colon", "caf%C3%A9", "ctl%01", "tab%09",
                     new string('t', 41)
                 })
        {
            using var put = await Send(client, HttpMethod.Put, Tag(inv, bad), bearer);
            using var delete = await Send(client, HttpMethod.Delete, Tag(inv, bad), bearer);
            Assert.AreEqual(HttpStatusCode.BadRequest, put.StatusCode, bad);
            Assert.AreEqual(HttpStatusCode.BadRequest, delete.StatusCode, bad);
            StringAssert.Contains(await put.Content.ReadAsStringAsync(), "invalid_tag");
        }

        Assert.AreEqual(0, store.Calls, "an invalid tag must never reach the store");

        // Positive control: boundary-length and mixed-case tags pass; the stored tag is lowercase.
        using var longest = await Send(client, HttpMethod.Put, Tag(inv, new string('t', 40)), bearer);
        Assert.AreEqual(HttpStatusCode.Created, longest.StatusCode);
        using var mixed = await Send(client, HttpMethod.Put, Tag(inv, "Key.Case_1-x"), bearer);
        Assert.AreEqual(HttpStatusCode.Created, mixed.StatusCode);
        Assert.AreEqual("key.case_1-x", store.LastTag);
        Assert.AreEqual(2, store.Calls);
    }

    [TestMethod]
    public async Task UnauthorizedInvestigationCallersNeverReachTheTagStore()
    {
        var store = new CountingStore();
        using var denied = new ApiFactory(store: store, shares: new FixedShares(false));
        using var client = denied.CreateClient();
        var inv = Guid.NewGuid();
        var analyst = Token("synthetic-analyst", "analyst", TenantA);
        var reviewer = Token("synthetic-reviewer", "reviewer", TenantA);

        using var put = await Send(client, HttpMethod.Put, Tag(inv, "t"), analyst);
        using var delete = await Send(client, HttpMethod.Delete, Tag(inv, "t"), analyst);
        using var list = await Send(client, HttpMethod.Get, Tags(inv), reviewer);
        Assert.AreEqual(HttpStatusCode.NotFound, put.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, list.StatusCode);
        Assert.AreEqual(0, store.Calls);

        // Positive control: the same requests reach the store when the share check passes.
        using var allowed = new ApiFactory(store: store, shares: new FixedShares(true));
        using var allowedClient = allowed.CreateClient();
        using var ok = await Send(allowedClient, HttpMethod.Put, Tag(inv, "t"), analyst);
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual(1, store.Calls);
    }

    [TestMethod]
    public async Task TagsArePrivateAuditedAndTenantIsolatedOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var bob = Token("synthetic-bob", "operator", TenantA);
        var aliceB = Token("synthetic-alice", "analyst", TenantB);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, alice);

        using var added = await Send(client, HttpMethod.Put, Tag(inv, "Urgent"), alice);
        Assert.AreEqual(HttpStatusCode.Created, added.StatusCode);
        var first = (await added.Content.ReadFromJsonAsync<TagDto>())!;
        Assert.AreEqual("urgent", first.Tag);
        using var replay = await Send(client, HttpMethod.Put, Tag(inv, "urgent"), alice);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(first, await replay.Content.ReadFromJsonAsync<TagDto>());
        using var second = await Send(client, HttpMethod.Put, Tag(inv, "a-first"), alice);
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);
        CollectionAssert.AreEqual(new[] { "a-first", "urgent" }, await ListTags(client, inv, alice));

        // A same-tenant caller without a grant sees what an unknown investigation answers.
        using var unknown = await Send(client, HttpMethod.Put, Tag(Guid.NewGuid(), "urgent"), bob);
        using var ungranted = await Send(client, HttpMethod.Put, Tag(inv, "urgent"), bob);
        using var ungrantedList = await Send(client, HttpMethod.Get, Tags(inv), bob);
        using var foreignTenant = await Send(client, HttpMethod.Put, Tag(inv, "urgent"), aliceB);
        Assert.AreEqual(HttpStatusCode.NotFound, ungranted.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, ungrantedList.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, foreignTenant.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.AreEqual(unknownBody, await ungranted.Content.ReadAsStringAsync());
        Assert.AreEqual(unknownBody, await foreignTenant.Content.ReadAsStringAsync());

        // After a grant, the grantee has an independent tag set: neither sees the other's tags.
        using var grant = await Send(client, HttpMethod.Put, $"/api/v1/tenant-investigations/{inv}/shares/synthetic-bob", alice);
        Assert.AreEqual(HttpStatusCode.Created, grant.StatusCode);
        using var bobAdd = await Send(client, HttpMethod.Put, Tag(inv, "bob-only"), bob);
        Assert.AreEqual(HttpStatusCode.Created, bobAdd.StatusCode);
        CollectionAssert.AreEqual(new[] { "bob-only" }, await ListTags(client, inv, bob));
        CollectionAssert.AreEqual(new[] { "a-first", "urgent" }, await ListTags(client, inv, alice));
        using var bobRemovesAlices = await Send(client, HttpMethod.Delete, Tag(inv, "urgent"), bob);
        Assert.AreEqual(HttpStatusCode.NoContent, bobRemovesAlices.StatusCode, "removing an absent own tag is a no-op");
        CollectionAssert.AreEqual(new[] { "a-first", "urgent" }, await ListTags(client, inv, alice));

        using var removed = await Send(client, HttpMethod.Delete, Tag(inv, "urgent"), alice);
        Assert.AreEqual(HttpStatusCode.NoContent, removed.StatusCode);
        CollectionAssert.AreEqual(new[] { "a-first" }, await ListTags(client, inv, alice));

        // One audit event per effective change (no replay, no no-op removal), attributed to the owner.
        var actions = await db.TenantAuditEvents.AsNoTracking()
            .Where(x => x.TenantId == TenantA && x.EntityType == EfTenantInvestigationTagStore.AuditEntityType)
            .Select(x => x.Action + "|" + x.ActorSubject + "|" + x.EntityId).ToListAsync();
        var id = inv.ToString("D");
        CollectionAssert.AreEquivalent(
            new[]
            {
                $"investigation.tag_added|synthetic-alice|{id}", $"investigation.tag_added|synthetic-alice|{id}",
                $"investigation.tag_added|synthetic-bob|{id}", $"investigation.tag_removed|synthetic-alice|{id}"
            },
            actions);
        Assert.AreEqual(0, await db.TenantInvestigationTags.AsNoTracking().CountAsync(x => x.TenantId == TenantB));
    }

    [TestMethod]
    public async Task ClosedInvestigationsRefuseTagChangesAndReopenRestoresThemOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, alice);
        using var kept = await Send(client, HttpMethod.Put, Tag(inv, "kept"), alice);
        Assert.AreEqual(HttpStatusCode.Created, kept.StatusCode);

        using var close = await Send(client, HttpMethod.Post, $"/api/v1/tenant-investigations/{inv}/close", alice, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.OK, close.StatusCode);
        using var addClosed = await Send(client, HttpMethod.Put, Tag(inv, "late"), alice);
        using var removeClosed = await Send(client, HttpMethod.Delete, Tag(inv, "kept"), alice);
        Assert.AreEqual(HttpStatusCode.Conflict, addClosed.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, removeClosed.StatusCode);
        StringAssert.Contains(await addClosed.Content.ReadAsStringAsync(), "investigation_closed");
        CollectionAssert.AreEqual(new[] { "kept" }, await ListTags(client, inv, alice), "reads still work while closed");

        // The database refuses the same writes even when the application check is bypassed.
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var insert = new NpgsqlCommand(
            "INSERT INTO tenant_investigation_tags (tenant_id, investigation_id, subject, tag, created_at_utc) VALUES (@t, @i, 'synthetic-alice', 'raw', now())",
            connection);
        insert.Parameters.AddWithValue("t", TenantA);
        insert.Parameters.AddWithValue("i", inv);
        var insertError = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.AreEqual("the investigation is closed", insertError.MessageText);
        await using var delete = new NpgsqlCommand("DELETE FROM tenant_investigation_tags WHERE tenant_id = @t", connection);
        delete.Parameters.AddWithValue("t", TenantA);
        var deleteError = await Assert.ThrowsExactlyAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
        Assert.AreEqual("the investigation is closed", deleteError.MessageText);

        using var reopen = await Send(client, HttpMethod.Post, $"/api/v1/tenant-investigations/{inv}/reopen", alice, new { expectedVersion = 1 });
        Assert.AreEqual(HttpStatusCode.OK, reopen.StatusCode);
        using var addOpen = await Send(client, HttpMethod.Put, Tag(inv, "late"), alice);
        using var removeOpen = await Send(client, HttpMethod.Delete, Tag(inv, "kept"), alice);
        Assert.AreEqual(HttpStatusCode.Created, addOpen.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, removeOpen.StatusCode);
        CollectionAssert.AreEqual(new[] { "late" }, await ListTags(client, inv, alice));
    }

    [TestMethod]
    public async Task ConcurrentAddsCannotExceedThePerInvestigationCapOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, alice);
        var cap = TenantInvestigationTagLimits.MaxPerSubjectPerInvestigation;

        var statuses = await Task.WhenAll(Enumerable.Range(0, cap + 8).Select(async i =>
        {
            using var response = await Send(client, HttpMethod.Put, Tag(inv, $"t{i}"), alice);
            return response.StatusCode;
        }));
        Assert.AreEqual(cap, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.AreEqual(8, statuses.Count(s => s == HttpStatusCode.Conflict));
        Assert.AreEqual(cap, (await ListTags(client, inv, alice)).Length);

        using var over = await Send(client, HttpMethod.Put, Tag(inv, "extra"), alice);
        Assert.AreEqual(HttpStatusCode.Conflict, over.StatusCode);
        StringAssert.Contains(await over.Content.ReadAsStringAsync(), "tag_limit_reached");
        // An existing tag still replays at the cap, and removing one frees a slot.
        using var replay = await Send(client, HttpMethod.Put, Tag(inv, "t0"), alice);
        Assert.IsTrue(replay.StatusCode is HttpStatusCode.OK);
        using var free = await Send(client, HttpMethod.Delete, Tag(inv, "t0"), alice);
        using var reuse = await Send(client, HttpMethod.Put, Tag(inv, "extra"), alice);
        Assert.AreEqual(HttpStatusCode.NoContent, free.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, reuse.StatusCode);

        // The cap is per subject: another subject with a grant is unaffected.
        var bob = Token("synthetic-bob", "analyst", TenantA);
        using var grant = await Send(client, HttpMethod.Put, $"/api/v1/tenant-investigations/{inv}/shares/synthetic-bob", alice);
        Assert.AreEqual(HttpStatusCode.Created, grant.StatusCode);
        using var other = await Send(client, HttpMethod.Put, Tag(inv, "extra"), bob);
        Assert.AreEqual(HttpStatusCode.Created, other.StatusCode);
    }

    [TestMethod]
    public async Task DatabaseRefusesCrossTenantRepointedAndMalformedTagsOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var investigations = new EfTenantInvestigationStore(db, TimeProvider.System);
        var invA = (await investigations.CreateAsync(TenantA, new InvestigationInput("k", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var invA2 = (await investigations.CreateAsync(TenantA, new InvestigationInput("k2", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var store = new EfTenantInvestigationTagStore(db, TimeProvider.System);
        Assert.AreEqual(TagAddOutcome.Added, (await store.AddAsync(TenantA, invA, "ok", "synthetic", CancellationToken.None)).Outcome);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        // Wrong tenant for the investigation, unknown investigation: guard trigger (23514).
        foreach (var (tenant, investigation) in new[] { (TenantB, invA), (TenantA, Guid.NewGuid()) })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_tags (tenant_id, investigation_id, subject, tag, created_at_utc) VALUES (@t, @i, 'other', 'x', now())",
                connection);
            insert.Parameters.AddWithValue("t", tenant);
            insert.Parameters.AddWithValue("i", investigation);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState);
        }

        // Malformed tags and subjects are refused by check constraints (positive control above: 'ok').
        foreach (var (subject, tag) in new[] { ("other", "Upper"), ("other", "-x"), ("other", ""), (" ", "x"), ("a\u0001b", "x") })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_tags (tenant_id, investigation_id, subject, tag, created_at_utc) VALUES (@t, @i, @s, @g, now())",
                connection);
            insert.Parameters.AddWithValue("t", TenantA);
            insert.Parameters.AddWithValue("i", invA);
            insert.Parameters.AddWithValue("s", subject);
            insert.Parameters.AddWithValue("g", tag);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState, $"{subject}/{tag}");
        }

        await using var update = new NpgsqlCommand("UPDATE tenant_investigation_tags SET investigation_id = @i", connection);
        update.Parameters.AddWithValue("i", invA2);
        var updateError = await Assert.ThrowsExactlyAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
        Assert.AreEqual("55000", updateError.SqlState);

        Assert.AreEqual(1, await db.TenantInvestigationTags.AsNoTracking().CountAsync());
        Assert.AreEqual(TagAddOutcome.NotFound, (await store.AddAsync(TenantB, invA, "ok", "synthetic", CancellationToken.None)).Outcome);
        Assert.IsNull(await store.ListAsync(TenantB, invA, "synthetic", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AddAsync(TenantA, invA, "Upper", "synthetic", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AddAsync(TenantA, invA, "ok", " ", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.AddAsync(Guid.Empty, invA, "ok", "synthetic", CancellationToken.None));
    }

    private static async Task<string[]> ListTags(HttpClient client, Guid investigation, string bearer)
    {
        using var response = await Send(client, HttpMethod.Get, Tags(investigation), bearer);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TagList>())!.Items.Select(i => i.Tag).ToArray();
    }

    private sealed record TagDto(Guid InvestigationId, string Tag, DateTime CreatedAtUtc);

    private sealed record TagList(List<TagDto> Items);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_tags";
        return builder.ConnectionString;
    }

    /// <summary>Store double that counts calls and records the accepted caller and tag.</summary>
    private sealed class CountingStore : ITenantInvestigationTagStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, string Subject)> Added { get; } = [];
        public string? LastTag { get; private set; }

        public Task<TagAddResult> AddAsync(
            Guid tenantId, Guid investigationId, string tag, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Added.Add((tenantId, subject));
            LastTag = tag;
            return Task.FromResult(new TagAddResult(
                TagAddOutcome.Added, new TenantInvestigationTagModel(investigationId, tag, subject, DateTime.UtcNow)));
        }

        public Task<TagRemoveOutcome> RemoveAsync(
            Guid tenantId, Guid investigationId, string tag, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(TagRemoveOutcome.Removed);
        }

        public Task<IReadOnlyList<TenantInvestigationTagModel>?> ListAsync(
            Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantInvestigationTagModel>?>([]);
        }
    }

    /// <summary>Share store double answering every authorization question the same way.</summary>
    private sealed class FixedShares(bool authorized) : ITenantInvestigationShareStore
    {
        public Task<bool> IsAuthorizedAsync(Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken) =>
            Task.FromResult(authorized);

        public Task<ShareGrantResult> GrantAsync(Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareRevokeOutcome> RevokeAsync(Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantInvestigationShareModel>?> ListActiveAsync(Guid tenantId, Guid investigationId, string owner, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> ListActiveGrantedInvestigationIdsAsync(Guid tenantId, string subject, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ApiFactory(
        string? connectionString = null, ITenantInvestigationTagStore? store = null, ITenantInvestigationShareStore? shares = null)
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
                if (shares is not null) services.AddSingleton(shares);
            });
        }
    }
}
