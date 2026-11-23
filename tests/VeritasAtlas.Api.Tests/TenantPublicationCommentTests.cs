using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Api.Controllers;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P1-02 slice 2: append-only editorial comments on a publication revision. Refusals and body validation
/// are proven before the store is touched; add, replay, key conflict, ordering, per-revision cap, audit,
/// tenant isolation and database guards run against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set
/// (own <c>*_revcomments</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantPublicationCommentTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private const string Revisions = "/api/v1/tenant-publication-revisions";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly string Digest = new('a', 64);

    private static string Token(string? subject, string role, Guid tenant) =>
        TenantInvestigationEvidenceItemTests.Sign(subject, [role], [tenant.ToString("D")]);

    private static Task<HttpResponseMessage> Send(HttpClient c, HttpMethod m, string path, string bearer, object? body = null) =>
        TenantInvestigationEvidenceItemTests.Send(c, m, path, bearer, body);

    private static string Comments(Guid id) => $"{Revisions}/{id}/comments";

    [TestMethod]
    public async Task RefusedCallersAndBadBodiesNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var a = TenantA.ToString("D");
        var id = Guid.NewGuid();
        var body = new { clientKey = Guid.NewGuid(), body = "synthetic comment" };

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["reviewer"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["reviewer"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["publisher", "reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["intruder"], [a]),
                     Token("s", "admin", TenantA),
                 })
        {
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Get })
            {
                using var response = await Send(client, method, Comments(id), bearer, method == HttpMethod.Post ? body : null);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
            }
        }

        using var anonymous = await client.GetAsync(Comments(id));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var reviewer = Token("synthetic-reviewer", "reviewer", TenantA);
        foreach (var bad in new object?[]
                 {
                     null, new { }, new { body = "x" }, new { clientKey = Guid.Empty, body = "x" },
                     new { clientKey = Guid.NewGuid() }, new { clientKey = Guid.NewGuid(), body = "" },
                     new { clientKey = Guid.NewGuid(), body = "   " }, new { clientKey = Guid.NewGuid(), body = "bad\u0007text" },
                     new { clientKey = Guid.NewGuid(), body = new string('c', 2001) }
                 })
        {
            using var response = await Send(client, HttpMethod.Post, Comments(id), reviewer, bad);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, JsonSerializer.Serialize(bad));
        }

        Assert.AreEqual(0, store.Calls, "a refused caller or invalid body must never reach the store");

        // Positive control: tenant and author come from the token, never from the query string, and the
        // 2000-character boundary plus line breaks are accepted.
        using var ok = await Send(client, HttpMethod.Post, $"{Comments(id)}?tenant={TenantB}&author=spoofed", reviewer,
            new { clientKey = Guid.NewGuid(), body = "  " + new string('c', 1990) + "\nline two  " });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, "synthetic-reviewer"), store.Added.Single());
        Assert.IsFalse(store.LastBody!.StartsWith(' ') || store.LastBody.EndsWith(' '), "the body is stored trimmed");
    }

    [TestMethod]
    public async Task WorstCaseUnicodeEscapedCommentIsAcceptedAndOversizedBodiesAreRefused()
    {
        // The in-process test server does not enforce [RequestSizeLimit], so pin the attribute itself: it must
        // cover the worst-case JSON (every character as a 6-byte escape) plus the envelope, and stay bounded.
        var limit = typeof(TenantPublicationCommentsController).GetMethod(nameof(TenantPublicationCommentsController.Add))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute), false)
            .Single();
        var bytes = (long)limit.GetType().GetField("_bytes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(limit)!;
        Assert.AreEqual((long)TenantPublicationCommentsController.MaxRequestBytes, bytes);
        Assert.IsTrue(bytes >= 6L * TenantPublicationCommentRules.MaxBodyLength + 512,
            "the limit must admit a 2000-character comment written entirely as unicode escapes");
        Assert.IsTrue(bytes <= 64 * 1024, "the limit must stay bounded");

        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var reviewer = Token("synthetic-reviewer", "reviewer", TenantA);
        var id = Guid.NewGuid();

        async Task<HttpResponseMessage> Raw(string json)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Comments(id));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reviewer);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }

        // 2000 valid characters, every one written as a \uXXXX escape: about 12 KB on the wire.
        var escaped = string.Concat(Enumerable.Repeat("\\u0061", TenantPublicationCommentRules.MaxBodyLength));
        var worstCase = $"{{\"clientKey\":\"{Guid.NewGuid()}\",\"body\":\"{escaped}\"}}";
        Assert.IsTrue(worstCase.Length > 8 * 1024, "the fixture must exceed the former 8 KiB limit");
        using var ok = await Raw(worstCase);
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual(TenantPublicationCommentRules.MaxBodyLength, store.LastBody!.Length);

        var oversized = $"{{\"clientKey\":\"{Guid.NewGuid()}\",\"body\":\"{new string('a', TenantPublicationCommentsController.MaxRequestBytes)}\"}}";
        using var refused = await Raw(oversized);
        Assert.IsTrue(refused.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, refused.StatusCode.ToString());
        Assert.AreEqual(1, store.Calls, "the oversized body never reaches the store");
    }

    [TestMethod]
    public async Task CommentHistoryRunsOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var author = Token("synthetic-author", "operator", TenantA);
        var id = await CreateDraftAsync(client, author);

        using var empty = await Send(client, HttpMethod.Get, Comments(id), author);
        Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);
        Assert.AreEqual(0, (await empty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("comments").GetArrayLength());

        var key1 = Guid.NewGuid();
        using var first = await Send(client, HttpMethod.Post, Comments(id), Token("rita", "reviewer", TenantA),
            new { clientKey = key1, body = "Please cite the primary source." });
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("rita", firstJson.GetProperty("author").GetString());

        // Idempotent retry returns the same comment, writes no second row and no second audit event.
        using var replay = await Send(client, HttpMethod.Post, Comments(id), Token("rita", "reviewer", TenantA),
            new { clientKey = key1, body = "Please cite the primary source." });
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(firstJson.GetProperty("id").GetGuid(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.AreEqual(1, await AuditCountAsync(cs));

        // The same key with different text, or from a different author, is a conflict and changes nothing.
        using var otherBody = await Send(client, HttpMethod.Post, Comments(id), Token("rita", "reviewer", TenantA),
            new { clientKey = key1, body = "Different text." });
        using var otherAuthor = await Send(client, HttpMethod.Post, Comments(id), Token("sam", "reviewer", TenantA),
            new { clientKey = key1, body = "Please cite the primary source." });
        Assert.AreEqual(HttpStatusCode.Conflict, otherBody.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, otherAuthor.StatusCode);
        StringAssert.Contains(await otherBody.Content.ReadAsStringAsync(), "comment_key_conflict");

        using var second = await Send(client, HttpMethod.Post, Comments(id), author,
            new { clientKey = Guid.NewGuid(), body = "Source added, ready for another look." });
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);
        Assert.AreEqual(2, await AuditCountAsync(cs));

        // Every lifecycle role reads the same ordered history.
        foreach (var role in new[] { "operator", "analyst", "reviewer", "publisher" })
        {
            using var list = await Send(client, HttpMethod.Get, Comments(id), Token("synthetic-" + role, role, TenantA));
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, role);
            var items = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("comments").EnumerateArray().ToList();
            CollectionAssert.AreEqual(new[] { "rita", "synthetic-author" }, items.Select(x => x.GetProperty("author").GetString()).ToArray(), role);
        }

        // Tenant isolation: another tenant sees an unknown id for both verbs, and nothing leaks into its audit.
        var foreign = Token("synthetic-reviewer", "reviewer", TenantB);
        using var foreignGet = await Send(client, HttpMethod.Get, Comments(id), foreign);
        using var foreignPost = await Send(client, HttpMethod.Post, Comments(id), foreign,
            new { clientKey = Guid.NewGuid(), body = "intrusion" });
        using var unknown = await Send(client, HttpMethod.Post, Comments(Guid.NewGuid()), author,
            new { clientKey = Guid.NewGuid(), body = "x" });
        Assert.AreEqual(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, foreignPost.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.AreEqual(2, await AuditCountAsync(cs));

        // The audit event names the actor and revision but never carries the comment text.
        await using (var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs)))
        {
            var events = await db.TenantAuditEvents.Where(x => x.Action == EfTenantPublicationCommentStore.AddedAction).ToListAsync();
            Assert.IsTrue(events.All(e => e.EntityId == id.ToString("D") && e.TenantId == TenantA));
            Assert.IsFalse(events.Any(e => (e.EventKey + e.Action + e.EntityId + e.ActorSubject).Contains("primary source")));
        }

        // Comments stay possible on a decided revision (dispute history) while the lifecycle is untouched.
        await TransitionAsync(client, author, id, "Submit", 0);
        await TransitionAsync(client, Token("rita", "reviewer", TenantA), id, "Approve", 1);
        using var late = await Send(client, HttpMethod.Post, Comments(id), Token("synthetic-publisher", "publisher", TenantA),
            new { clientKey = Guid.NewGuid(), body = "Noted after approval." });
        Assert.AreEqual(HttpStatusCode.Created, late.StatusCode);
    }

    [TestMethod]
    public async Task PerRevisionCapRefusesTheNextCommentOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var author = Token("synthetic-author", "operator", TenantA);
        var id = await CreateDraftAsync(client, author);
        var other = await CreateDraftAsync(client, author);

        await using (var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs)))
        {
            for (var i = 0; i < TenantPublicationCommentRules.MaxPerRevision; i++)
            {
                db.TenantPublicationComments.Add(new TenantPublicationComment
                {
                    TenantId = TenantA, Id = Guid.NewGuid(), RevisionId = id, ClientKey = Guid.NewGuid(),
                    AuthorSubject = "bulk", Body = $"bulk {i}", CreatedAtUtc = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }

        using var full = await Send(client, HttpMethod.Post, Comments(id), author, new { clientKey = Guid.NewGuid(), body = "one too many" });
        Assert.AreEqual(HttpStatusCode.Conflict, full.StatusCode);
        StringAssert.Contains(await full.Content.ReadAsStringAsync(), "comment_limit_reached");
        using var list = await Send(client, HttpMethod.Get, Comments(id), author);
        Assert.AreEqual(TenantPublicationCommentRules.MaxPerRevision,
            (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("comments").GetArrayLength());

        // Positive control: the cap is per revision, so another revision still accepts a comment.
        using var ok = await Send(client, HttpMethod.Post, Comments(other), author, new { clientKey = Guid.NewGuid(), body = "fine" });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
    }

    [TestMethod]
    public async Task DatabaseRefusesCrossTenantMalformedAndMutatedCommentsOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var id = await CreateDraftAsync(client, Token("synthetic-author", "operator", TenantA));
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();

        async Task<PostgresException> Fail(string sql, Guid tenant, string text)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("t", tenant);
            command.Parameters.AddWithValue("r", id);
            command.Parameters.AddWithValue("b", text);
            return await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        const string insert = "INSERT INTO tenant_publication_comments " +
            "(tenant_id, id, revision_id, client_key, author_subject, body, created_at_utc) " +
            "VALUES (@t, gen_random_uuid(), @r, gen_random_uuid(), 'rita', @b, now())";
        Assert.AreEqual("23514", (await Fail(insert, TenantB, "x")).SqlState, "revision of another tenant");
        Assert.AreEqual("23514", (await Fail(insert, TenantA, "bad\u0007text")).SqlState, "control character");
        Assert.AreEqual("23514", (await Fail(insert, TenantA, "   ")).SqlState, "blank body");
        Assert.AreEqual("22001", (await Fail(insert, TenantA, new string('c', 2001))).SqlState, "over-long body");

        // Positive control: a well-formed row is accepted, then no update, delete or truncate may touch it.
        await using (var ok = new NpgsqlCommand(insert, connection))
        {
            ok.Parameters.AddWithValue("t", TenantA);
            ok.Parameters.AddWithValue("r", id);
            ok.Parameters.AddWithValue("b", "kept");
            Assert.AreEqual(1, await ok.ExecuteNonQueryAsync());
        }

        Assert.AreEqual("55000", (await Fail("UPDATE tenant_publication_comments SET body = @b WHERE tenant_id = @t AND revision_id = @r", TenantA, "edited")).SqlState);
        Assert.AreEqual("55000", (await Fail("DELETE FROM tenant_publication_comments WHERE tenant_id = @t AND revision_id = @r AND body <> @b", TenantA, "none")).SqlState);
        Assert.AreEqual("55000", (await Fail("TRUNCATE tenant_publication_comments", TenantA, "none")).SqlState);
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient client, string author)
    {
        using var created = await Send(client, HttpMethod.Post, Revisions, author,
            new { contentDigest = Digest, policyPermitsPublicDisplay = true });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task TransitionAsync(HttpClient client, string bearer, Guid id, string action, long version)
    {
        using var response = await Send(client, HttpMethod.Post, $"{Revisions}/{id}/transitions", bearer,
            new { action, expectedVersion = version, operationId = $"op-{action}-{id:N}" });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, action);
    }

    private static async Task<int> AuditCountAsync(string cs)
    {
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        return await db.TenantAuditEvents.CountAsync(x => x.Action == EfTenantPublicationCommentStore.AddedAction);
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
        builder.Database += "_revcomments";
        return builder.ConnectionString;
    }

    /// <summary>Store double that counts calls and records the accepted caller and body.</summary>
    private sealed class CountingStore : ITenantPublicationCommentStore
    {
        private int _calls;
        public int Calls => _calls;
        public string? LastBody { get; private set; }
        public List<(Guid Tenant, string Author)> Added { get; } = [];

        public Task<CommentAddResult> AddAsync(
            Guid tenantId, Guid revisionId, Guid clientKey, string body, string author, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Added.Add((tenantId, author));
            LastBody = body;
            return Task.FromResult(new CommentAddResult(
                CommentAddOutcome.Created,
                new TenantPublicationCommentModel(Guid.NewGuid(), revisionId, clientKey, author, body, DateTime.UtcNow)));
        }

        public Task<IReadOnlyList<TenantPublicationCommentModel>?> ListAsync(
            Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantPublicationCommentModel>?>(null);
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantPublicationCommentStore? store = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0007");
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
