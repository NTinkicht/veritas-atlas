using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-10 slice 2 (#210): collecting verified evidence into a tenant investigation.
/// Refusals are proven before the store is touched; collection, replay, conflict, audit,
/// append-only schema and cross-tenant denial run against real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_invitems</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationEvidenceItemTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private const string Doc = "<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Section one.</P1></Legislation>";

    private static string ItemsPath(Guid investigation) => $"/api/v1/tenant-investigations/{investigation}/evidence-items";

    [TestMethod]
    public async Task RefusedCallersAndInvalidInputNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store, shares: new FixedShares(true));
        using var client = factory.CreateClient();
        var path = ItemsPath(Guid.NewGuid());
        var a = TenantA.ToString("D");
        var body = new { evidenceVerificationId = Guid.NewGuid() };

        foreach (var bearer in new[]
                 {
                     Sign("s", ["analyst"], []), Sign("s", ["analyst"], ["not-a-uuid"]),
                     Sign("s", ["analyst"], [a, TenantB.ToString("D")]), Sign("s", [], [a]),
                     Sign("s", ["analyst", "reviewer"], [a]), Sign(null, ["analyst"], [a]), Sign("a\u0001b", ["analyst"], [a]),
                 })
        {
            using var post = await Send(client, HttpMethod.Post, path, bearer, body);
            Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode);
        }

        foreach (var role in new[] { "reviewer", "publisher", "admin" })
        {
            using var post = await Send(client, HttpMethod.Post, path, Sign("s", [role], [a]), body);
            Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode, role);
            StringAssert.Contains(await post.Content.ReadAsStringAsync(), "author_required", role);
        }

        using var listNoTenant = await Send(client, HttpMethod.Get, path, Sign("s", ["analyst"], []));
        using var listNoRole = await Send(client, HttpMethod.Get, path, Sign("s", ["anonymous"], [a]));
        using var anonymous = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Forbidden, listNoTenant.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, listNoRole.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var analyst = Sign("synthetic-analyst", ["analyst"], [a]);
        foreach (var invalid in new object[]
                 {
                     new { }, new { evidenceVerificationId = Guid.Empty },
                     new { evidenceVerificationId = Guid.NewGuid(), note = " " },
                     new { evidenceVerificationId = Guid.NewGuid(), note = " lead" },
                     new { evidenceVerificationId = Guid.NewGuid(), note = "a\nb" },
                     new { evidenceVerificationId = Guid.NewGuid(), note = new string('n', TenantInvestigationEvidenceLimits.MaxNoteLength + 1) },
                 })
        {
            using var response = await Send(client, HttpMethod.Post, path, analyst, invalid);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.AreEqual(0, store.Calls, "a refused caller or invalid input must never reach the store");

        var verification = Guid.NewGuid();
        using var ok = await Send(client, HttpMethod.Post, $"{path}?tenant={TenantB}", analyst,
            new { evidenceVerificationId = verification, note = "n", tenantId = TenantB });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, verification, "n", "synthetic-analyst"), store.Collected.Single());
    }

    [TestMethod]
    public async Task UnauthorizedInvestigationCallersNeverReachTheEvidenceStore()
    {
        var store = new CountingStore();
        var path = ItemsPath(Guid.NewGuid());
        var analyst = Sign("synthetic-stranger", ["analyst"], [TenantA.ToString("D")]);
        var reviewer = Sign("synthetic-stranger", ["reviewer"], [TenantA.ToString("D")]);
        var body = new { evidenceVerificationId = Guid.NewGuid() };
        var compare = $"{path}/comparison?left={Guid.NewGuid()}&right={Guid.NewGuid()}";

        using (var denied = new ApiFactory(store: store, shares: new FixedShares(false)))
        using (var client = denied.CreateClient())
        {
            var responses = new List<string>();
            foreach (var (method, url, bearer, payload) in new (HttpMethod, string, string, object?)[]
                     {
                         (HttpMethod.Post, path, analyst, body), (HttpMethod.Get, path, reviewer, null), (HttpMethod.Get, compare, reviewer, null),
                     })
            {
                using var response = await Send(client, method, url, bearer, payload);
                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, url);
                var text = await response.Content.ReadAsStringAsync();
                StringAssert.Contains(text, "tenant_investigation_or_evidence_not_found");
                responses.Add(text);
            }

            Assert.AreEqual(1, responses.Distinct().Count(), "the three refusals share one body");
            Assert.AreEqual(0, store.Calls, "an unauthorized caller must never reach the evidence store");
        }

        // Positive control: the same requests with an authorizing share store reach the store.
        using var allowed = new ApiFactory(store: store, shares: new FixedShares(true));
        using var allowedClient = allowed.CreateClient();
        using var created = await Send(allowedClient, HttpMethod.Post, path, analyst, body);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        using var listed = await Send(allowedClient, HttpMethod.Get, path, reviewer);
        Assert.AreEqual(HttpStatusCode.NotFound, listed.StatusCode, "CountingStore lists null, proving the store was reached");
        Assert.AreEqual(2, store.Calls);

        // Actor-less readers are refused before any lookup.
        using var noActor = await Send(allowedClient, HttpMethod.Get, path, Sign(null, ["reviewer"], [TenantA.ToString("D")]));
        Assert.AreEqual(HttpStatusCode.Forbidden, noActor.StatusCode);
        StringAssert.Contains(await noActor.Content.ReadAsStringAsync(), "actor_required");
        Assert.AreEqual(2, store.Calls);
    }

    [TestMethod]
    public async Task OnlyCreatorAndActiveGranteesReachCollectedEvidenceOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verification = await SeedVerifiedAsync(db, TenantA, "a");
        var a = TenantA.ToString("D");
        var owner = Sign("synthetic-owner", ["analyst"], [a]);
        var otherAnalyst = Sign("synthetic-other-analyst", ["analyst"], [a]);
        var otherReviewer = Sign("synthetic-other-reviewer", ["reviewer"], [a]);
        var inv = await CreateInvestigationAsync(client, owner);

        using var collected = await Send(client, HttpMethod.Post, ItemsPath(inv), owner, new { evidenceVerificationId = verification });
        var item = (await collected.Content.ReadFromJsonAsync<Item>())!;
        using var collectedTwo = await Send(client, HttpMethod.Post, ItemsPath(inv), owner,
            new { evidenceVerificationId = await SeedVerifiedAsync(db, TenantA, "a2") });
        var itemTwo = (await collectedTwo.Content.ReadFromJsonAsync<Item>())!;
        var compare = $"{ItemsPath(inv)}/comparison?left={item.Id}&right={itemTwo.Id}";

        async Task<string> Probe(string bearer)
        {
            using var post = await Send(client, HttpMethod.Post, ItemsPath(inv), bearer, new { evidenceVerificationId = verification });
            using var list = await Send(client, HttpMethod.Get, ItemsPath(inv), bearer);
            using var cmp = await Send(client, HttpMethod.Get, compare, bearer);
            return $"{(int)post.StatusCode},{(int)list.StatusCode},{(int)cmp.StatusCode}";
        }

        // Ungranted same-tenant callers are refused exactly like an unknown investigation.
        using var unknownList = await Send(client, HttpMethod.Get, ItemsPath(Guid.NewGuid()), otherReviewer);
        // (A reviewer may never collect: its POST stays 403 author_required, before and after a grant.)
        foreach (var (bearer, expected) in new[] { (otherAnalyst, "404,404,404"), (otherReviewer, "403,404,404") })
        {
            Assert.AreEqual(expected, await Probe(bearer));
            using var list = await Send(client, HttpMethod.Get, ItemsPath(inv), bearer);
            Assert.AreEqual(await unknownList.Content.ReadAsStringAsync(), await list.Content.ReadAsStringAsync());
        }

        Assert.AreEqual(2, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync(), "refused collects wrote nothing");

        // Positive control: grants open the gate, per grantee and per investigation; the creator is unaffected.
        foreach (var grantee in new[] { "synthetic-other-analyst", "synthetic-other-reviewer" })
        {
            using var grant = await Send(client, HttpMethod.Put, $"/api/v1/tenant-investigations/{inv}/shares/{grantee}", owner);
            Assert.AreEqual(HttpStatusCode.Created, grant.StatusCode);
        }

        Assert.AreEqual("200,200,200", await Probe(otherAnalyst));
        Assert.AreEqual("403,200,200", await Probe(otherReviewer));

        // Revocation closes the gate again.
        using var revoke = await Send(client, HttpMethod.Delete, $"/api/v1/tenant-investigations/{inv}/shares/synthetic-other-analyst", owner);
        Assert.AreEqual(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.AreEqual("404,404,404", await Probe(otherAnalyst));
        Assert.AreEqual("200,200,200", await Probe(owner));
    }

    [TestMethod]
    public async Task CollectReplayConflictAuditAndCrossTenantDenialOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verificationA = await SeedVerifiedAsync(db, TenantA, "a");
        var verificationB = await SeedVerifiedAsync(db, TenantB, "b");
        var analystA = Sign("synthetic-analyst-a", ["analyst"], [TenantA.ToString("D")]);
        var reviewerA = Sign("synthetic-reviewer-a", ["reviewer"], [TenantA.ToString("D")]);
        var analystB = Sign("synthetic-analyst-b", ["analyst"], [TenantB.ToString("D")]);
        var invA = await CreateInvestigationAsync(client, analystA);
        var invB = await CreateInvestigationAsync(client, analystB);

        using var created = await Send(client, HttpMethod.Post, ItemsPath(invA), analystA,
            new { evidenceVerificationId = verificationA, note = "Key passage" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var first = (await created.Content.ReadFromJsonAsync<Item>())!;
        Assert.AreEqual((invA, verificationA, "Key passage", "synthetic-analyst-a"),
            (first.InvestigationId, first.EvidenceVerificationId, first.Note, first.CollectedBySubject));

        using var replay = await Send(client, HttpMethod.Post, ItemsPath(invA), analystA,
            new { evidenceVerificationId = verificationA, note = "Key passage" });
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(first, await replay.Content.ReadFromJsonAsync<Item>());

        using var conflict = await Send(client, HttpMethod.Post, ItemsPath(invA), analystA,
            new { evidenceVerificationId = verificationA, note = "Different" });
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        StringAssert.Contains(await conflict.Content.ReadAsStringAsync(), "note_conflict");

        // Another tenant's investigation or evidence answers exactly like an unknown one.
        using var crossInvestigation = await Send(client, HttpMethod.Post, ItemsPath(invB), analystA, new { evidenceVerificationId = verificationA });
        using var crossEvidence = await Send(client, HttpMethod.Post, ItemsPath(invA), analystA, new { evidenceVerificationId = verificationB });
        using var unknown = await Send(client, HttpMethod.Post, ItemsPath(Guid.NewGuid()), analystA, new { evidenceVerificationId = Guid.NewGuid() });
        foreach (var response in new[] { crossInvestigation, crossEvidence, unknown })
        {
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        }

        using var grantReviewer = await Send(client, HttpMethod.Put, $"/api/v1/tenant-investigations/{invA}/shares/synthetic-reviewer-a", analystA);
        Assert.AreEqual(HttpStatusCode.Created, grantReviewer.StatusCode);
        using var listed = await Send(client, HttpMethod.Get, ItemsPath(invA), reviewerA);
        Assert.AreEqual(HttpStatusCode.OK, listed.StatusCode);
        CollectionAssert.AreEqual(new[] { first.Id }, (await listed.Content.ReadFromJsonAsync<ItemList>())!.Items.Select(x => x.Id).ToArray());
        using var listCross = await Send(client, HttpMethod.Get, ItemsPath(invB), analystA);
        Assert.AreEqual(HttpStatusCode.NotFound, listCross.StatusCode);

        // Exactly one collection audit event, in its own tenant; refusals write none.
        var audits = await db.TenantAuditEvents.AsNoTracking().Where(e => e.Action == "investigation.evidence_collected").ToListAsync();
        Assert.AreEqual(1, audits.Count);
        Assert.AreEqual((TenantA, first.Id.ToString("D"), "synthetic-analyst-a"), (audits[0].TenantId, audits[0].EntityId, audits[0].ActorSubject));
    }

    [TestMethod]
    public async Task SchemaIsAppendOnlyAndGuardsReferencesOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verificationA = await SeedVerifiedAsync(db, TenantA, "a");
        var verificationB = await SeedVerifiedAsync(db, TenantB, "b");
        var investigationA = (await new EfTenantInvestigationStore(db, TimeProvider.System).CreateAsync(
            TenantA, new InvestigationInput("k", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var store = new EfTenantInvestigationEvidenceStore(db, TimeProvider.System);
        var item = (await store.CollectAsync(
            TenantA, investigationA, new EvidenceItemInput(verificationA, null, "synthetic"), CancellationToken.None)).Item!;
        Assert.IsNull(item.Note);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        foreach (var sql in new[]
                 {
                     "UPDATE tenant_investigation_evidence_items SET note = 'x'",
                     "DELETE FROM tenant_investigation_evidence_items",
                     "TRUNCATE tenant_investigation_evidence_items",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.AreEqual("55000", ex.SqlState, sql);
        }

        // The guard refuses a cross-tenant reference even when written around the store.
        foreach (var (investigation, verification) in new[] { (investigationA, verificationB), (Guid.NewGuid(), verificationA) })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_evidence_items (tenant_id, id, investigation_id, evidence_verification_id, collected_by_subject, created_at_utc) " +
                "VALUES (@t, @i, @inv, @v, 's', now())", connection);
            insert.Parameters.AddWithValue("t", TenantA);
            insert.Parameters.AddWithValue("i", Guid.NewGuid());
            insert.Parameters.AddWithValue("inv", investigation);
            insert.Parameters.AddWithValue("v", verification);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState);
        }
    }

    [TestMethod]
    public async Task ComparisonRefusalsNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store, shares: new FixedShares(true));
        using var client = factory.CreateClient();
        var path = $"{ItemsPath(Guid.NewGuid())}/comparison";
        var a = TenantA.ToString("D");
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();

        using var noTenant = await Send(client, HttpMethod.Get, $"{path}?left={x}&right={y}", Sign("s", ["analyst"], []));
        using var noRole = await Send(client, HttpMethod.Get, $"{path}?left={x}&right={y}", Sign("s", ["anonymous"], [a]));
        using var anonymous = await client.GetAsync($"{path}?left={x}&right={y}");
        Assert.AreEqual(HttpStatusCode.Forbidden, noTenant.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var analyst = Sign("s", ["analyst"], [a]);
        foreach (var query in new[] { "", $"?left={x}", $"?left={x}&right={x}", $"?left={Guid.Empty}&right={y}", "?left=nope&right=x" })
        {
            using var response = await Send(client, HttpMethod.Get, path + query, analyst);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, query);
        }
        Assert.AreEqual(0, store.Calls);
    }

    [TestMethod]
    public async Task ComparisonIsTenantScopedAndReadOnlyOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verificationA1 = await SeedVerifiedAsync(db, TenantA, "a1");
        var verificationA2 = await SeedVerifiedAsync(db, TenantA, "a2");
        var verificationB = await SeedVerifiedAsync(db, TenantB, "b");
        var analystA = Sign("synthetic-analyst-a", ["analyst"], [TenantA.ToString("D")]);
        var reviewerA = Sign("synthetic-reviewer-a", ["reviewer"], [TenantA.ToString("D")]);
        var analystB = Sign("synthetic-analyst-b", ["analyst"], [TenantB.ToString("D")]);
        var invA = await CreateInvestigationAsync(client, analystA);
        var invA2 = await CreateInvestigationAsync(client, analystA, "k2");
        var invB = await CreateInvestigationAsync(client, analystB);
        async Task<Item> Collect(Guid inv, string bearer, Guid verification, string? note)
        {
            using var r = await Send(client, HttpMethod.Post, ItemsPath(inv), bearer, new { evidenceVerificationId = verification, note });
            Assert.AreEqual(HttpStatusCode.Created, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<Item>())!;
        }

        var item1 = await Collect(invA, analystA, verificationA1, "first");
        var item2 = await Collect(invA, analystA, verificationA2, null);
        var itemElsewhere = await Collect(invA2, analystA, verificationA1, null);
        var itemB = await Collect(invB, analystB, verificationB, null);
        using var grantReviewer = await Send(client, HttpMethod.Put, $"/api/v1/tenant-investigations/{invA}/shares/synthetic-reviewer-a", analystA);
        Assert.AreEqual(HttpStatusCode.Created, grantReviewer.StatusCode);
        var auditsBefore = await db.TenantAuditEvents.AsNoTracking().CountAsync();

        using var ok = await Send(client, HttpMethod.Get, $"{ItemsPath(invA)}/comparison?left={item1.Id}&right={item2.Id}", reviewerA);
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, await ok.Content.ReadAsStringAsync());
        var body = await ok.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.AreEqual(item1.Id, body.GetProperty("left").GetProperty("itemId").GetGuid());
        Assert.AreEqual("first", body.GetProperty("left").GetProperty("note").GetString());
        Assert.AreEqual(verificationA2, body.GetProperty("right").GetProperty("evidenceVerificationId").GetGuid());
        Assert.IsFalse(body.GetProperty("sameRawRetrieval").GetBoolean());
        Assert.IsFalse(body.GetProperty("sameAttestation").GetBoolean());

        // Swapping sides mirrors the comparison; the same evidence in two items compares identical.
        using var swapped = await Send(client, HttpMethod.Get, $"{ItemsPath(invA)}/comparison?left={item2.Id}&right={item1.Id}", analystA);
        Assert.AreEqual(item2.Id, (await swapped.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("left").GetProperty("itemId").GetGuid());

        // Cross-tenant investigation, cross-tenant item, an item of another investigation and unknown ids all answer alike.
        using var unknown = await Send(client, HttpMethod.Get, $"{ItemsPath(Guid.NewGuid())}/comparison?left={item1.Id}&right={item2.Id}", analystA);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        foreach (var path in new[]
                 {
                     $"{ItemsPath(invB)}/comparison?left={item1.Id}&right={item2.Id}",
                     $"{ItemsPath(invA)}/comparison?left={item1.Id}&right={itemB.Id}",
                     $"{ItemsPath(invA)}/comparison?left={item1.Id}&right={itemElsewhere.Id}",
                     $"{ItemsPath(invA)}/comparison?left={item1.Id}&right={Guid.NewGuid()}",
                 })
        {
            using var response = await Send(client, HttpMethod.Get, path, analystA);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
            Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync(), path);
        }

        Assert.AreEqual(auditsBefore, await db.TenantAuditEvents.AsNoTracking().CountAsync(), "comparison is read-only");
    }

    private sealed record Item(Guid Id, Guid InvestigationId, Guid EvidenceVerificationId, string? Note, string CollectedBySubject, DateTime CreatedAtUtc);

    private sealed record ItemList(List<Item> Items);

    private sealed record Created(Guid Id);

    internal static async Task<Guid> CreateInvestigationAsync(HttpClient client, string bearer, string key = "k1")
    {
        using var response = await Send(client, HttpMethod.Post, "/api/v1/tenant-investigations", bearer, new { creationKey = key, title = "Case" });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private sealed class EchoVerifier : IPublisherBindingVerifier
    {
        public Task<PublisherBindingVerification> VerifyAsync(AttestationClaim c, CancellationToken ct) =>
            Task.FromResult(new PublisherBindingVerification(
                c.PublisherIdentity, c.BindingMechanism, c.BindingReference, c.FinalUri, c.ContentSha256, true));
    }

    internal static async Task<Guid> SeedVerifiedAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var at = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var recorded = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant,
            new RawRetrievalInput(source!.Id, key, "https://www.legislation.gov.uk/synthetic/act/1/data.xml", at, Encoding.UTF8.GetBytes(Doc)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, recorded.Outcome);
        var retrieval = recorded.Record!.Id;
        var run = (await new EfEvidenceParseService(db, TimeProvider.System)
            .ParseAsync(tenant, retrieval, "synthetic-seeder", CancellationToken.None)).Run!.Id;
        var attested = await new EfTenantRetrievalAttestationLedger(db, TimeProvider.System).RecordAsync(
            tenant,
            new RetrievalAttestationInput(
                retrieval, "k-" + key, "synthetic-retriever", "synthetic-publisher", PublisherBindingMechanism.SourceRegistry,
                "registry-entry-1", SourceAuthenticationResult.Authenticated, "gb-uk", AttestedSourceType.Statute, at.AddMinutes(5)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, attested.Outcome);
        var verify = await new EfEvidenceVerificationService(db, new EchoVerifier(), TimeProvider.System)
            .VerifyAsync(tenant, run, new EvidenceClassificationExpectation("gb-uk", "statute"), "synthetic-verifier", CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.Verified, verify.Outcome);
        return verify.Verification!.Id;
    }

    internal static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string bearer, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
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
        builder.Database += "_invitems";
        return builder.ConnectionString;
    }

    internal static DbContextOptions<VeritasAtlasDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options;

    internal static async Task ResetAsync(string connectionString)
    {
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    internal static string Sign(string? name, string[] roles, string[] tenants)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")) };
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(tenants.Select(t => new Claim(TenantClaim.Type, t)));
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: RuntimeSecurity.DefaultJwtIssuer,
            audience: RuntimeSecurity.DefaultJwtAudience,
            claims: claims,
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)),
                SecurityAlgorithms.HmacSha256)));
    }

    /// <summary>Store double that counts calls and records accepted collections.</summary>
    private sealed class CountingStore : ITenantInvestigationEvidenceStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, Guid Verification, string? Note, string Subject)> Collected { get; } = [];

        public Task<EvidenceItemCollectResult> CollectAsync(
            Guid tenantId, Guid investigationId, EvidenceItemInput input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Collected.Add((tenantId, input.EvidenceVerificationId, input.Note, input.CollectedBySubject));
            var model = new TenantInvestigationEvidenceItemModel(
                Guid.NewGuid(), investigationId, input.EvidenceVerificationId, input.Note, input.CollectedBySubject, DateTime.UtcNow);
            return Task.FromResult(new EvidenceItemCollectResult(EvidenceItemCollectOutcome.Collected, model));
        }

        public Task<EvidenceItemBatchResult> CollectBatchAsync(
            Guid tenantId, Guid investigationId, IReadOnlyList<EvidenceItemInput> inputs, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantInvestigationEvidenceItemModel>?> ListAsync(
            Guid tenantId, Guid investigationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantInvestigationEvidenceItemModel>?>(null);
        }

        public Task<TenantInvestigationEvidenceComparisonModel?> CompareAsync(
            Guid tenantId, Guid investigationId, Guid leftItemId, Guid rightItemId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TenantInvestigationEvidenceComparisonModel?>(null);
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
