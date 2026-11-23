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
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P1-02 slice 1: publisher-assigned reviewer on a publication revision. Refusals and body validation are
/// proven before the store is touched; assign, replay, change, clear, audit, author denial, decided-revision
/// refusal, tenant isolation and database guards run against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c>
/// is set (own <c>*_revreviewer</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantPublicationReviewerAssignmentTests
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

    private static string Reviewer(Guid id) => $"{Revisions}/{id}/reviewer";

    [TestMethod]
    public async Task RefusedCallersAndBadBodiesNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var a = TenantA.ToString("D");
        var id = Guid.NewGuid();
        var body = new { reviewer = "synthetic-reviewer" };

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["publisher"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["publisher"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["publisher", "reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["publisher"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["publisher"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["intruder"], [a]),
                     Token("s", "admin", TenantA),
                 })
        {
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Get })
            {
                using var response = await Send(client, method, Reviewer(id), bearer, method == HttpMethod.Put ? body : null);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
            }
        }

        foreach (var role in new[] { "operator", "analyst", "reviewer" })
        {
            using var put = await Send(client, HttpMethod.Put, Reviewer(id), Token("s", role, TenantA), body);
            using var delete = await Send(client, HttpMethod.Delete, Reviewer(id), Token("s", role, TenantA));
            Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode, role);
            Assert.AreEqual(HttpStatusCode.Forbidden, delete.StatusCode, role);
        }

        using var anonymous = await client.GetAsync(Reviewer(id));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var publisher = Token("synthetic-publisher", "publisher", TenantA);
        foreach (var bad in new object?[] { null, new { }, new { reviewer = "" }, new { reviewer = "   " },
                     new { reviewer = "bad\u0007name" }, new { reviewer = new string('r', 201) } })
        {
            using var response = await Send(client, HttpMethod.Put, Reviewer(id), publisher, bad);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, JsonSerializer.Serialize(bad));
        }

        Assert.AreEqual(0, store.Calls, "a refused caller or invalid body must never reach the store");

        // Positive control: tenant and actor come from the token, never from the query string.
        using var ok = await Send(client, HttpMethod.Put, $"{Reviewer(id)}?tenant={TenantB}&assignedBy=spoofed",
            publisher, new { reviewer = "  synthetic-reviewer  " });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, "synthetic-reviewer", "synthetic-publisher"), store.Assigned.Single());
        using var read = await Send(client, HttpMethod.Get, Reviewer(id), Token("synthetic-analyst", "analyst", TenantA));
        Assert.AreEqual(HttpStatusCode.NotFound, read.StatusCode);
        Assert.AreEqual(2, store.Calls);
    }

    [TestMethod]
    public async Task AssignmentLifecycleAuditAndRefusalsRunOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var author = Token("synthetic-author", "operator", TenantA);
        var publisher = Token("synthetic-publisher", "publisher", TenantA);
        var id = await CreateDraftAsync(client, author);

        // Positive control and role gate: a non-publisher cannot write, the publisher can.
        using var byAnalyst = await Send(client, HttpMethod.Put, Reviewer(id), Token("synthetic-analyst", "analyst", TenantA), new { reviewer = "rita" });
        Assert.AreEqual(HttpStatusCode.Forbidden, byAnalyst.StatusCode);
        using var none = await Send(client, HttpMethod.Get, Reviewer(id), author);
        Assert.AreEqual(HttpStatusCode.NotFound, none.StatusCode);
        StringAssert.Contains(await none.Content.ReadAsStringAsync(), "reviewer_not_assigned");

        using var created = await Send(client, HttpMethod.Put, Reviewer(id), publisher, new { reviewer = "rita" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("rita", first.GetProperty("reviewer").GetString());
        Assert.AreEqual("synthetic-publisher", first.GetProperty("assignedBy").GetString());

        using var replay = await Send(client, HttpMethod.Put, Reviewer(id), publisher, new { reviewer = "rita" });
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(1, await AuditCountAsync(cs, EfPublicationAction.Assigned), "a replay writes no audit event");

        using var changed = await Send(client, HttpMethod.Put, Reviewer(id), publisher, new { reviewer = "sam" });
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        Assert.AreEqual(2, await AuditCountAsync(cs, EfPublicationAction.Assigned));
        foreach (var role in new[] { "operator", "analyst", "reviewer", "publisher" })
        {
            using var read = await Send(client, HttpMethod.Get, Reviewer(id), Token("synthetic-" + role, role, TenantA));
            Assert.AreEqual(HttpStatusCode.OK, read.StatusCode, role);
            Assert.AreEqual("sam", (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reviewer").GetString());
        }

        // Separation of duties: the author cannot be the reviewer, and nothing changed.
        using var self = await Send(client, HttpMethod.Put, Reviewer(id), publisher, new { reviewer = "synthetic-author" });
        Assert.AreEqual(HttpStatusCode.Forbidden, self.StatusCode);
        StringAssert.Contains(await self.Content.ReadAsStringAsync(), "self_review_denied");

        // Tenant isolation: another tenant's publisher sees an unknown id for every verb.
        var foreign = Token("synthetic-publisher", "publisher", TenantB);
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Get })
        {
            using var response = await Send(client, method, Reviewer(id), foreign, method == HttpMethod.Put ? new { reviewer = "mallory" } : null);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, method.Method);
        }

        using var unknown = await Send(client, HttpMethod.Put, Reviewer(Guid.NewGuid()), publisher, new { reviewer = "rita" });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        using var cleared = await Send(client, HttpMethod.Delete, Reviewer(id), publisher);
        Assert.AreEqual(HttpStatusCode.NoContent, cleared.StatusCode);
        using var clearedAgain = await Send(client, HttpMethod.Delete, Reviewer(id), publisher);
        Assert.AreEqual(HttpStatusCode.NoContent, clearedAgain.StatusCode);
        Assert.AreEqual(1, await AuditCountAsync(cs, EfPublicationAction.Unassigned), "a no-op clear writes no audit event");

        // A decided revision refuses both writes while the lifecycle itself is untouched.
        using var assignAgain = await Send(client, HttpMethod.Put, Reviewer(id), publisher, new { reviewer = "rita" });
        Assert.AreEqual(HttpStatusCode.Created, assignAgain.StatusCode);
        await TransitionAsync(client, author, id, "Submit", 0);
        using var stillOpen = await Send(client, HttpMethod.Put, Reviewer(id), publisher, new { reviewer = "sam" });
        Assert.AreEqual(HttpStatusCode.OK, stillOpen.StatusCode, "a submitted revision may be reassigned");
        await TransitionAsync(client, Token("rita", "reviewer", TenantA), id, "Approve", 1);
        using var putClosed = await Send(client, HttpMethod.Put, Reviewer(id), publisher, new { reviewer = "rita" });
        using var deleteClosed = await Send(client, HttpMethod.Delete, Reviewer(id), publisher);
        Assert.AreEqual(HttpStatusCode.Conflict, putClosed.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, deleteClosed.StatusCode);
        StringAssert.Contains(await putClosed.Content.ReadAsStringAsync(), "revision_closed");
        using var readClosed = await Send(client, HttpMethod.Get, Reviewer(id), publisher);
        Assert.AreEqual("sam", (await readClosed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reviewer").GetString());
    }

    [TestMethod]
    public async Task DatabaseRefusesAuthorCrossTenantAndRepointedAssignmentsOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var id = await CreateDraftAsync(client, Token("synthetic-author", "operator", TenantA));
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();

        async Task<PostgresException> Fail(string sql, Guid tenant, string reviewer)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("t", tenant);
            command.Parameters.AddWithValue("r", id);
            command.Parameters.AddWithValue("s", reviewer);
            return await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        const string insert = "INSERT INTO tenant_publication_reviewer_assignments " +
            "(tenant_id, revision_id, reviewer_subject, assigned_by, assigned_at_utc) VALUES (@t, @r, @s, 'p', now())";
        Assert.AreEqual("23514", (await Fail(insert, TenantA, "synthetic-author")).SqlState, "author as reviewer");
        Assert.AreEqual("23514", (await Fail(insert, TenantB, "rita")).SqlState, "revision of another tenant");
        Assert.AreEqual("23514", (await Fail(insert, TenantA, "bad\u0007name")).SqlState, "malformed reviewer");

        // Positive control: a well-formed row is accepted, and its key then cannot move.
        await using (var ok = new NpgsqlCommand(insert, connection))
        {
            ok.Parameters.AddWithValue("t", TenantA);
            ok.Parameters.AddWithValue("r", id);
            ok.Parameters.AddWithValue("s", "rita");
            Assert.AreEqual(1, await ok.ExecuteNonQueryAsync());
        }

        var moved = await Fail("UPDATE tenant_publication_reviewer_assignments SET revision_id = gen_random_uuid() WHERE tenant_id = @t AND revision_id = @r AND reviewer_subject <> @s", TenantA, "none");
        Assert.AreEqual("55000", moved.SqlState);
        var toAuthor = await Fail("UPDATE tenant_publication_reviewer_assignments SET reviewer_subject = @s WHERE tenant_id = @t AND revision_id = @r", TenantA, "synthetic-author");
        Assert.AreEqual("23514", toAuthor.SqlState);
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

    private static async Task<int> AuditCountAsync(string cs, string action)
    {
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        return await db.TenantAuditEvents.CountAsync(x => x.Action == action);
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
        builder.Database += "_revreviewer";
        return builder.ConnectionString;
    }

    private static class EfPublicationAction
    {
        public const string Assigned = "publication.reviewer_assigned";
        public const string Unassigned = "publication.reviewer_unassigned";
    }

    /// <summary>Store double that counts calls and records the accepted caller and reviewer.</summary>
    private sealed class CountingStore : ITenantPublicationReviewerAssignmentStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, string Reviewer, string AssignedBy)> Assigned { get; } = [];

        public Task<ReviewerAssignResult> AssignAsync(
            Guid tenantId, Guid revisionId, string reviewer, string assignedBy, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Assigned.Add((tenantId, reviewer, assignedBy));
            return Task.FromResult(new ReviewerAssignResult(
                ReviewerAssignOutcome.Created,
                new TenantPublicationReviewerAssignmentModel(revisionId, reviewer, assignedBy, DateTime.UtcNow)));
        }

        public Task<ReviewerUnassignOutcome> UnassignAsync(
            Guid tenantId, Guid revisionId, string actor, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(ReviewerUnassignOutcome.NotFound);
        }

        public Task<ReviewerGetResult> GetAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ReviewerGetResult(true, null));
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantPublicationReviewerAssignmentStore? store = null)
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
