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
/// VER-P0-10 slice 4: append-only annotations on collected evidence items. Refusals are proven
/// before the store is touched; add, replay, ordering, audit, append-only schema, same-tenant
/// guard and cross-tenant denial run against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is
/// set (own <c>*_annot</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationAnnotationTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Path(Guid investigation, Guid item) =>
        $"/api/v1/tenant-investigations/{investigation}/evidence-items/{item}/annotations";

    [TestMethod]
    public async Task RefusedCallersAndInvalidInputNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var path = Path(Guid.NewGuid(), Guid.NewGuid());
        var a = TenantA.ToString("D");
        var body = new { text = "Check the date" };

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], ["not-a-uuid"]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["analyst"], [a]),
                 })
        {
            using var post = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Post, path, bearer, body);
            Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode);
        }

        foreach (var role in new[] { "reviewer", "publisher", "admin" })
        {
            using var post = await TenantInvestigationEvidenceItemTests.Send(
                client, HttpMethod.Post, path, TenantInvestigationEvidenceItemTests.Sign("s", [role], [a]), body);
            Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode, role);
            StringAssert.Contains(await post.Content.ReadAsStringAsync(), "author_required", role);
        }

        using var listNoTenant = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Get, path, TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []));
        using var listNoRole = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Get, path, TenantInvestigationEvidenceItemTests.Sign("s", ["anonymous"], [a]));
        using var anonymous = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Forbidden, listNoTenant.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, listNoRole.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var analyst = TenantInvestigationEvidenceItemTests.Sign("synthetic-analyst", ["analyst"], [a]);
        foreach (var invalid in new object[]
                 {
                     new { }, new { text = "" }, new { text = " " }, new { text = " lead" }, new { text = "trail " },
                     new { text = "a\u0001b" }, new { text = "a\tb" }, new { text = "a\rb" },
                     new { text = new string('n', TenantInvestigationAnnotationLimits.MaxTextLength + 1) },
                 })
        {
            using var response = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Post, path, analyst, invalid);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, System.Text.Json.JsonSerializer.Serialize(invalid));
        }
        Assert.AreEqual(0, store.Calls, "a refused caller or invalid input must never reach the store");

        var item = Guid.NewGuid();
        using var ok = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, $"{Path(Guid.NewGuid(), item)}?tenant={TenantB}", analyst,
            new { text = "line one\nline two", tenantId = TenantB, authorSubject = "spoofed" });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, item, "line one\nline two", "synthetic-analyst"), store.Added.Single());
    }

    [TestMethod]
    public async Task AddReplayAuditOrderingAndCrossTenantDenialOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var verificationA = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var analystA = TenantInvestigationEvidenceItemTests.Sign("synthetic-analyst-a", ["analyst"], [TenantA.ToString("D")]);
        var reviewerA = TenantInvestigationEvidenceItemTests.Sign("synthetic-reviewer-a", ["reviewer"], [TenantA.ToString("D")]);
        var analystB = TenantInvestigationEvidenceItemTests.Sign("synthetic-analyst-b", ["analyst"], [TenantB.ToString("D")]);
        var invA = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analystA);
        var invA2 = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analystA, "k2");
        var invB = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analystB);
        var itemA = (await new EfTenantInvestigationEvidenceStore(db, TimeProvider.System).CollectAsync(
            TenantA, invA, new EvidenceItemInput(verificationA, null, "synthetic"), CancellationToken.None)).Item!.Id;

        using var created = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invA, itemA), analystA, new { text = "Verify the as-of date" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var first = (await created.Content.ReadFromJsonAsync<Annotation>())!;
        Assert.AreEqual((invA, itemA, "Verify the as-of date", "synthetic-analyst-a"),
            (first.InvestigationId, first.EvidenceItemId, first.Text, first.AuthorSubject));

        using var replay = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invA, itemA), analystA, new { text = "Verify the as-of date" });
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(first, await replay.Content.ReadFromJsonAsync<Annotation>());

        // Same text by another author is a distinct annotation; different text is distinct too.
        using var other = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invA, itemA),
            TenantInvestigationEvidenceItemTests.Sign("synthetic-operator-a", ["operator"], [TenantA.ToString("D")]),
            new { text = "Verify the as-of date" });
        using var second = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invA, itemA), analystA, new { text = "Second thought" });
        Assert.AreEqual(HttpStatusCode.Created, other.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);

        // Another tenant's ids, a mismatched investigation and unknown ids answer identically.
        using var unknown = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invA, Guid.NewGuid()), analystA, new { text = "x" });
        using var crossInvestigation = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invB, itemA), analystA, new { text = "x" });
        using var crossCaller = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invA, itemA), analystB, new { text = "x" });
        using var wrongInvestigation = await TenantInvestigationEvidenceItemTests.Send(
            client, HttpMethod.Post, Path(invA2, itemA), analystA, new { text = "x" });
        foreach (var response in new[] { crossInvestigation, crossCaller, wrongInvestigation, unknown })
        {
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        }

        using var listed = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Get, Path(invA, itemA), reviewerA);
        Assert.AreEqual(HttpStatusCode.OK, listed.StatusCode);
        var items = (await listed.Content.ReadFromJsonAsync<AnnotationList>())!.Items;
        Assert.AreEqual(3, items.Count);
        Assert.AreEqual(first.Id, items[0].Id);
        CollectionAssert.AreEqual(items.OrderBy(x => x.CreatedAtUtc).ToList(), items);
        using var listCross = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Get, Path(invA, itemA), analystB);
        using var listWrong = await TenantInvestigationEvidenceItemTests.Send(client, HttpMethod.Get, Path(invA2, itemA), analystA);
        Assert.AreEqual(HttpStatusCode.NotFound, listCross.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, listWrong.StatusCode);

        // Exactly one audit event per stored annotation (replay and refusals write none).
        var audits = await db.TenantAuditEvents.AsNoTracking()
            .Where(e => e.Action == "investigation.annotation_added").ToListAsync();
        Assert.AreEqual(3, audits.Count);
        Assert.IsTrue(audits.All(e => e.TenantId == TenantA));
        Assert.IsTrue(audits.Any(e => e.EntityId == first.Id.ToString("D") && e.ActorSubject == "synthetic-analyst-a"));
    }

    [TestMethod]
    public async Task ConcurrentIdenticalAnnotationsStoreExactlyOneRowOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var verification = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var analyst = TenantInvestigationEvidenceItemTests.Sign("synthetic-analyst-a", ["analyst"], [TenantA.ToString("D")]);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, analyst);
        var item = (await new EfTenantInvestigationEvidenceStore(db, TimeProvider.System).CollectAsync(
            TenantA, inv, new EvidenceItemInput(verification, null, "synthetic"), CancellationToken.None)).Item!.Id;

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await TenantInvestigationEvidenceItemTests.Send(
                client, HttpMethod.Post, Path(inv, item), analyst, new { text = "Racing note" });
            return (response.StatusCode, (await response.Content.ReadFromJsonAsync<Annotation>())!.Id);
        }));

        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.AreEqual(1, responses.Select(r => r.Item2).Distinct().Count());
        Assert.AreEqual(1, await db.TenantInvestigationAnnotations.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "investigation.annotation_added"));
    }

    [TestMethod]
    public async Task SchemaIsAppendOnlyAndGuardsReferencesOverRealPostgres()
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
        var store = new EfTenantInvestigationAnnotationStore(db, TimeProvider.System);
        var added = await store.AddAsync(TenantA, invA, item, new AnnotationInput("kept", "synthetic"), CancellationToken.None);
        Assert.AreEqual(AnnotationAddOutcome.Added, added.Outcome);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        foreach (var sql in new[]
                 {
                     "UPDATE tenant_investigation_annotations SET text = 'x'",
                     "DELETE FROM tenant_investigation_annotations",
                     "TRUNCATE tenant_investigation_annotations",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.AreEqual("55000", ex.SqlState, sql);
        }

        // The database itself refuses an annotation whose item is another tenant's, another
        // investigation's or unknown, even when the application layer is bypassed.
        foreach (var (tenant, investigation, evidenceItem) in new[]
                 {
                     (TenantB, invA, item), (TenantA, invA2, item), (TenantA, invA, Guid.NewGuid()),
                 })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_annotations (tenant_id, id, investigation_id, evidence_item_id, text, author_subject, created_at_utc) VALUES (@t, @id, @i, @e, 'x', 's', now())",
                connection);
            insert.Parameters.AddWithValue("t", tenant);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("i", investigation);
            insert.Parameters.AddWithValue("e", evidenceItem);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState);
        }

        Assert.AreEqual(1, await db.TenantInvestigationAnnotations.AsNoTracking().CountAsync());
        Assert.AreEqual(AnnotationAddOutcome.NotFound,
            (await store.AddAsync(TenantB, invA, item, new AnnotationInput("x", "synthetic"), CancellationToken.None)).Outcome);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.AddAsync(TenantA, invA, item, new AnnotationInput(" bad", "synthetic"), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.AddAsync(Guid.Empty, invA, item, new AnnotationInput("ok", "synthetic"), CancellationToken.None));
    }

    private sealed record Annotation(
        Guid Id, Guid InvestigationId, Guid EvidenceItemId, string Text, string AuthorSubject, DateTime CreatedAtUtc);

    private sealed record AnnotationList(List<Annotation> Items);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_annot";
        return builder.ConnectionString;
    }

    /// <summary>Store double that counts calls and records accepted annotations.</summary>
    private sealed class CountingStore : ITenantInvestigationAnnotationStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, Guid Item, string Text, string Subject)> Added { get; } = [];

        public Task<AnnotationAddResult> AddAsync(
            Guid tenantId, Guid investigationId, Guid evidenceItemId, AnnotationInput input,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Added.Add((tenantId, evidenceItemId, input.Text, input.AuthorSubject));
            var model = new TenantInvestigationAnnotationModel(
                Guid.NewGuid(), investigationId, evidenceItemId, input.Text, input.AuthorSubject, DateTime.UtcNow);
            return Task.FromResult(new AnnotationAddResult(AnnotationAddOutcome.Added, model));
        }

        public Task<IReadOnlyList<TenantInvestigationAnnotationModel>?> ListAsync(
            Guid tenantId, Guid investigationId, Guid evidenceItemId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantInvestigationAnnotationModel>?>(null);
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantInvestigationAnnotationStore? store = null)
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
