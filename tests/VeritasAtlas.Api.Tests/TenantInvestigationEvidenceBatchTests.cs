using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P1-01: batch import of verified evidence into an investigation. Refusals are proven
/// before the store is touched; atomicity, idempotency, conflicts, closed investigations,
/// audit and cross-tenant denial run against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c>
/// is set (own <c>*_invbatch</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationEvidenceBatchTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string BatchPath(Guid investigation) => $"/api/v1/tenant-investigations/{investigation}/evidence-items/batch";

    private static string ItemsPath(Guid investigation) => $"/api/v1/tenant-investigations/{investigation}/evidence-items";

    [TestMethod]
    public async Task RefusedCallersAndInvalidBatchesNeverReachTheStore()
    {
        var store = new RecordingStore();
        using var factory = new ApiFactory(store: store, shares: new FixedShares(true));
        using var client = factory.CreateClient();
        var path = BatchPath(Guid.NewGuid());
        var a = TenantA.ToString("D");
        var good = new { items = new[] { new { evidenceVerificationId = Guid.NewGuid(), note = "n" } } };
        var sign = TenantInvestigationEvidenceItemTests.Sign;
        var send = TenantInvestigationEvidenceItemTests.Send;

        foreach (var bearer in new[]
                 {
                     sign("s", ["analyst"], []), sign("s", ["analyst"], [a, TenantB.ToString("D")]), sign("s", [], [a]),
                     sign("s", ["analyst", "reviewer"], [a]), sign(null, ["analyst"], [a]), sign("a\u0001b", ["analyst"], [a]),
                 })
        {
            using var post = await send(client, HttpMethod.Post, path, bearer, good);
            Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode);
        }

        foreach (var role in new[] { "reviewer", "publisher", "admin" })
        {
            using var post = await send(client, HttpMethod.Post, path, sign("s", [role], [a]), good);
            Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode, role);
            StringAssert.Contains(await post.Content.ReadAsStringAsync(), "author_required", role);
        }

        using var anonymous = await client.PostAsJsonAsync(path, good);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var analyst = sign("synthetic-analyst", ["analyst"], [a]);
        var id = Guid.NewGuid();
        var tooMany = Enumerable.Range(0, TenantInvestigationEvidenceLimits.MaxBatchSize + 1)
            .Select(_ => new { evidenceVerificationId = Guid.NewGuid() }).ToArray();
        foreach (var (invalid, code) in new (object, string)[]
                 {
                     (new { }, "invalid_batch"),
                     (new { items = Array.Empty<object>() }, "invalid_batch"),
                     (new { items = tooMany }, "invalid_batch"),
                     (new { items = new object[] { new { } } }, "invalid_evidence_verification_id"),
                     (new { items = new[] { new { evidenceVerificationId = Guid.Empty } } }, "invalid_evidence_verification_id"),
                     (new { items = new[] { new { evidenceVerificationId = id, note = " lead" } } }, "invalid_note"),
                     (new { items = new[] { new { evidenceVerificationId = id, note = "a\nb" } } }, "invalid_note"),
                     (new { items = new[] { new { evidenceVerificationId = id }, new { evidenceVerificationId = id } } }, "duplicate_evidence_in_batch"),
                 })
        {
            using var response = await send(client, HttpMethod.Post, path, analyst, invalid);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, code);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), code);
        }

        Assert.AreEqual(0, store.Batches.Count, "a refused caller or invalid batch must never reach the store");

        // Positive control: the tenant and actor come only from the token, never from the body.
        var verification = Guid.NewGuid();
        using var ok = await send(client, HttpMethod.Post, $"{path}?tenant={TenantB}", analyst,
            new { tenantId = TenantB, items = new[] { new { evidenceVerificationId = verification, note = "n", collectedBySubject = "evil" } } });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        var (tenant, inputs) = store.Batches.Single();
        Assert.AreEqual(TenantA, tenant);
        Assert.AreEqual(new EvidenceItemInput(verification, "n", "synthetic-analyst"), inputs.Single());
    }

    [TestMethod]
    public async Task UnauthorizedInvestigationCallersNeverReachTheBatchStore()
    {
        var store = new RecordingStore();
        var analyst = TenantInvestigationEvidenceItemTests.Sign("synthetic-stranger", ["analyst"], [TenantA.ToString("D")]);
        var body = new { items = new[] { new { evidenceVerificationId = Guid.NewGuid() } } };

        using (var denied = new ApiFactory(store: store, shares: new FixedShares(false)))
        using (var client = denied.CreateClient())
        {
            using var response = await TenantInvestigationEvidenceItemTests.Send(
                client, HttpMethod.Post, BatchPath(Guid.NewGuid()), analyst, body);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "tenant_investigation_or_evidence_not_found");
            Assert.AreEqual(0, store.Batches.Count);
        }

        using var allowed = new ApiFactory(store: store, shares: new FixedShares(true));
        using var allowedClient = allowed.CreateClient();
        using var ok = await TenantInvestigationEvidenceItemTests.Send(
            allowedClient, HttpMethod.Post, BatchPath(Guid.NewGuid()), analyst, body);
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual(1, store.Batches.Count);
    }

    [TestMethod]
    public async Task BatchIsAtomicIdempotentAndAuditedOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var v1 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a1");
        var v2 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a2");
        var v3 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a3");
        var vB = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantB, "b");
        var analyst = TenantInvestigationEvidenceItemTests.Sign("synthetic-analyst-a", ["analyst"], [TenantA.ToString("D")]);
        var analystB = TenantInvestigationEvidenceItemTests.Sign("synthetic-analyst-b", ["analyst"], [TenantB.ToString("D")]);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analyst);
        var invB = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analystB);
        var send = TenantInvestigationEvidenceItemTests.Send;

        using var first = await send(client, HttpMethod.Post, BatchPath(inv), analyst,
            new { items = new object[] { new { evidenceVerificationId = v1, note = "one" }, new { evidenceVerificationId = v2 } } });
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());
        var created = (await first.Content.ReadFromJsonAsync<BatchResponse>())!.Items;
        CollectionAssert.AreEqual(new[] { v1, v2 }, created.Select(x => x.Item.EvidenceVerificationId).ToArray(), "input order is kept");
        Assert.IsTrue(created.All(x => !x.Replayed));
        Assert.AreEqual("one", created[0].Item.Note);
        Assert.AreEqual("synthetic-analyst-a", created[1].Item.CollectedBySubject);

        // Idempotent: the same batch replays every row and writes nothing.
        using var again = await send(client, HttpMethod.Post, BatchPath(inv), analyst,
            new { items = new object[] { new { evidenceVerificationId = v2 }, new { evidenceVerificationId = v1, note = "one" } } });
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        var replayed = (await again.Content.ReadFromJsonAsync<BatchResponse>())!.Items;
        Assert.IsTrue(replayed.All(x => x.Replayed));
        CollectionAssert.AreEquivalent(created.Select(x => x.Item.Id).ToArray(), replayed.Select(x => x.Item.Id).ToArray());

        // A mixed batch collects only the new entry and replays the known one.
        using var mixed = await send(client, HttpMethod.Post, BatchPath(inv), analyst,
            new { items = new object[] { new { evidenceVerificationId = v1, note = "one" }, new { evidenceVerificationId = v3 } } });
        Assert.AreEqual(HttpStatusCode.Created, mixed.StatusCode);
        var mixedItems = (await mixed.Content.ReadFromJsonAsync<BatchResponse>())!.Items;
        CollectionAssert.AreEqual(new[] { true, false }, mixedItems.Select(x => x.Replayed).ToArray());
        Assert.AreEqual(3, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync());

        // Atomic refusals: a note conflict, unknown evidence and cross-tenant evidence each write nothing,
        // even when earlier entries in the same batch were new.
        var fresh = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a4");
        using var conflict = await send(client, HttpMethod.Post, BatchPath(inv), analyst,
            new { items = new object[] { new { evidenceVerificationId = fresh }, new { evidenceVerificationId = v1, note = "different" } } });
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictText = await conflict.Content.ReadAsStringAsync();
        StringAssert.Contains(conflictText, "note_conflict");
        StringAssert.Contains(conflictText, "items[1]");

        using var unknown = await send(client, HttpMethod.Post, BatchPath(inv), analyst,
            new { items = new object[] { new { evidenceVerificationId = fresh }, new { evidenceVerificationId = Guid.NewGuid() } } });
        using var crossEvidence = await send(client, HttpMethod.Post, BatchPath(inv), analyst,
            new { items = new object[] { new { evidenceVerificationId = fresh }, new { evidenceVerificationId = vB } } });
        using var crossInvestigation = await send(client, HttpMethod.Post, BatchPath(invB), analyst,
            new { items = new object[] { new { evidenceVerificationId = fresh } } });
        using var unknownInvestigation = await send(client, HttpMethod.Post, BatchPath(Guid.NewGuid()), analyst,
            new { items = new object[] { new { evidenceVerificationId = fresh } } });
        foreach (var response in new[] { unknown, crossEvidence, crossInvestigation, unknownInvestigation })
        {
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        }

        Assert.AreEqual(3, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync(), "refused batches wrote no row");
        Assert.AreEqual(0, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync(x => x.EvidenceVerificationId == fresh));

        // Exactly one audit event per collected item, none for replays or refusals.
        var audits = await db.TenantAuditEvents.AsNoTracking().Where(e => e.Action == "investigation.evidence_collected").ToListAsync();
        Assert.AreEqual(3, audits.Count);
        Assert.IsTrue(audits.All(e => e.TenantId == TenantA && e.ActorSubject == "synthetic-analyst-a"));

        // Positive control: the items are readable through the ordinary list endpoint.
        using var listed = await send(client, HttpMethod.Get, ItemsPath(inv), analyst);
        Assert.AreEqual(3, (await listed.Content.ReadFromJsonAsync<ItemList>())!.Items.Count);
    }

    [TestMethod]
    public async Task ClosedInvestigationRefusesBatchAndReopenAllowsItOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var v1 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a1");
        var analyst = TenantInvestigationEvidenceItemTests.Sign("synthetic-analyst-a", ["analyst"], [TenantA.ToString("D")]);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analyst);
        var send = TenantInvestigationEvidenceItemTests.Send;
        var body = new { items = new object[] { new { evidenceVerificationId = v1 } } };

        using var close = await send(client, HttpMethod.Post, $"/api/v1/tenant-investigations/{inv}/close", analyst, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.OK, close.StatusCode, await close.Content.ReadAsStringAsync());
        using var refused = await send(client, HttpMethod.Post, BatchPath(inv), analyst, body);
        Assert.AreEqual(HttpStatusCode.Conflict, refused.StatusCode);
        StringAssert.Contains(await refused.Content.ReadAsStringAsync(), "investigation_closed");
        Assert.AreEqual(0, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync());

        using var reopen = await send(client, HttpMethod.Post, $"/api/v1/tenant-investigations/{inv}/reopen", analyst, new { expectedVersion = 1 });
        Assert.AreEqual(HttpStatusCode.OK, reopen.StatusCode, await reopen.Content.ReadAsStringAsync());
        using var accepted = await send(client, HttpMethod.Post, BatchPath(inv), analyst, body);
        Assert.AreEqual(HttpStatusCode.Created, accepted.StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentOverlappingBatchesCollectEachEvidenceOnceOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        await using var seed = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var v1 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(seed, TenantA, "a1");
        var v2 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(seed, TenantA, "a2");
        var inv = (await new EfTenantInvestigationStore(seed, TimeProvider.System).CreateAsync(
            TenantA, new InvestigationInput("k", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;

        async Task<EvidenceItemBatchResult> Run(Guid[] ids)
        {
            await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
            return await new EfTenantInvestigationEvidenceStore(db, TimeProvider.System).CollectBatchAsync(
                TenantA, inv, ids.Select(x => new EvidenceItemInput(x, null, "synthetic")).ToList(), CancellationToken.None);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => Run(i % 2 == 0 ? [v1, v2] : [v2, v1]))));
        Assert.IsTrue(results.All(r => r.Outcome == EvidenceItemCollectOutcome.Collected));
        Assert.AreEqual(2, await seed.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync(), "each evidence was collected exactly once");
        Assert.AreEqual(2, await seed.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "investigation.evidence_collected"));
        Assert.AreEqual(1, results.SelectMany(r => r.Entries!).Count(e => !e.Replayed && e.Item.EvidenceVerificationId == v1));
    }

    private sealed record Item(Guid Id, Guid InvestigationId, Guid EvidenceVerificationId, string? Note, string CollectedBySubject, DateTime CreatedAtUtc);

    private sealed record Entry(Item Item, bool Replayed);

    private sealed record BatchResponse(List<Entry> Items);

    private sealed record ItemList(List<Item> Items);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_invbatch";
        return builder.ConnectionString;
    }

    /// <summary>Store double that records accepted batches.</summary>
    private sealed class RecordingStore : ITenantInvestigationEvidenceStore
    {
        public List<(Guid Tenant, IReadOnlyList<EvidenceItemInput> Inputs)> Batches { get; } = [];

        public Task<EvidenceItemBatchResult> CollectBatchAsync(
            Guid tenantId, Guid investigationId, IReadOnlyList<EvidenceItemInput> inputs, CancellationToken cancellationToken)
        {
            Batches.Add((tenantId, inputs));
            var entries = inputs.Select(i => new EvidenceItemBatchEntry(
                new TenantInvestigationEvidenceItemModel(
                    Guid.NewGuid(), investigationId, i.EvidenceVerificationId, i.Note, i.CollectedBySubject, DateTime.UtcNow), false)).ToList();
            return Task.FromResult(new EvidenceItemBatchResult(EvidenceItemCollectOutcome.Collected, entries, null));
        }

        public Task<EvidenceItemCollectResult> CollectAsync(
            Guid tenantId, Guid investigationId, EvidenceItemInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantInvestigationEvidenceItemModel>?> ListAsync(
            Guid tenantId, Guid investigationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TenantInvestigationEvidenceComparisonModel?> CompareAsync(
            Guid tenantId, Guid investigationId, Guid leftItemId, Guid rightItemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

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
        string? connectionString = null, ITenantInvestigationEvidenceStore? store = null, ITenantInvestigationShareStore? shares = null)
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
