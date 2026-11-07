using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-16 slice 5: publication revision HTTP endpoints. Callers without exactly one workflow role are refused
/// before the store; the lifecycle, self-approval denial, version races and idempotency are exercised end to end
/// against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_pubrevapi</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantPublicationRevisionEndpointTests
{
    private const string SigningSecret = "publication-revision-api-test-only-key-not-for-production-01";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly string Digest = new('a', 64);
    private const string Base = "/api/v1/tenant-publication-revisions";

    [TestMethod]
    public async Task CallersWithoutExactlyOneWorkflowRoleAreRefusedBeforeTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        var bearers = new (string Label, string Bearer)[]
        {
            ("admin", Sign("synthetic-admin", ["admin"], [TenantA.ToString("D")])),
            ("no role", Sign("synthetic-none", [], [TenantA.ToString("D")])),
            ("unknown role", Sign("synthetic-x", ["administrator"], [TenantA.ToString("D")])),
            ("two roles", Sign("synthetic-two", ["operator", "reviewer"], [TenantA.ToString("D")])),
            ("no tenant", Sign("synthetic-op", ["operator"], [])),
            ("two tenants", Sign("synthetic-op", ["operator"], [TenantA.ToString("D"), TenantB.ToString("D")])),
            ("no subject", Sign(null, ["operator"], [TenantA.ToString("D")])),
        };
        foreach (var (label, bearer) in bearers)
        {
            foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
            {
                (HttpMethod.Post, Base, new { contentDigest = Digest, policyPermitsPublicDisplay = true }),
                (HttpMethod.Get, $"{Base}/{id}", null),
                (HttpMethod.Put, $"{Base}/{id}/visibility", new { expectedFactsVersion = 0, operationId = "op-v" }),
                (HttpMethod.Post, $"{Base}/{id}/transitions",
                    new { action = "Submit", expectedVersion = 0, operationId = "op-1" }),
            })
            {
                var response = await Send(client, method, path, bearer, body);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, $"{label} {method} {path}");
            }
        }

        Assert.AreEqual(0, store.Calls, "A refused caller must never reach the store.");

        using var anonymous = await client.GetAsync($"{Base}/{id}");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task OnlyAnAuthorMayCreateADraftAndBadBodiesAreRejected()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var tenant = TenantA.ToString("D");

        foreach (var role in new[] { "reviewer", "publisher" })
        {
            var refused = await Send(client, HttpMethod.Post, Base, Sign("synthetic-" + role, [role], [tenant]),
                new { contentDigest = Digest, policyPermitsPublicDisplay = true });
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, role);
        }

        var operatorBearer = Sign("synthetic-op", ["operator"], [tenant]);
        foreach (var bad in new object?[]
        {
            null,
            new { },
            new { contentDigest = Digest },
            new { policyPermitsPublicDisplay = true },
            new { contentDigest = "not-a-digest", policyPermitsPublicDisplay = true },
            new { contentDigest = new string('A', 64), policyPermitsPublicDisplay = true },
            new { contentDigest = Digest + "\n", policyPermitsPublicDisplay = true },
        })
        {
            var response = await Send(client, HttpMethod.Post, Base, operatorBearer, bad);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, JsonSerializer.Serialize(bad));
        }

        var id = Guid.NewGuid();
        foreach (var bad in new object?[]
        {
            null,
            new { },
            new { action = "Submit", expectedVersion = 0 },
            new { action = "submit", expectedVersion = 0, operationId = "o" },
            new { action = "7", expectedVersion = 0, operationId = "o" },
            new { action = "Bogus", expectedVersion = 0, operationId = "o" },
            new { action = "Submit", expectedVersion = -1, operationId = "o" },
            new { action = "Submit", expectedVersion = 0, operationId = " " },
            new { action = "Submit", expectedVersion = 0, operationId = new string('x', 201) },
            new { action = "Submit", expectedVersion = 0, operationId = "bad\u0007id" },
        })
        {
            var response = await Send(client, HttpMethod.Post, $"{Base}/{id}/transitions", operatorBearer, bad);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, JsonSerializer.Serialize(bad));
        }

        Assert.AreEqual(0, store.Calls);
    }

    [TestMethod]
    public async Task ReviewedAndPublishedLifecycleRunsEndToEndWithSeparatedDuties()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();
        var tenant = TenantA.ToString("D");
        var author = Sign("synthetic-author", ["operator"], [tenant]);
        var reviewer = Sign("synthetic-reviewer", ["reviewer"], [tenant]);
        var publisher = Sign("synthetic-publisher", ["publisher"], [tenant]);

        var created = await Send(client, HttpMethod.Post, Base, author,
            new { contentDigest = Digest, policyPermitsPublicDisplay = true });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var revision = (await created.Content.ReadFromJsonAsync<JsonElement>());
        var id = revision.GetProperty("id").GetGuid();
        Assert.AreEqual("Draft", revision.GetProperty("state").GetString());
        Assert.AreEqual("synthetic-author", revision.GetProperty("authorId").GetString());
        Assert.AreEqual(0, revision.GetProperty("version").GetInt64());

        // Approving a draft is an invalid transition; a reviewer cannot submit.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Transition(client, reviewer, id, "Approve", 0, "o-bad-1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Transition(client, reviewer, id, "Submit", 0, "o-bad-2")).StatusCode);

        var submitted = await Transition(client, author, id, "Submit", 0, "o-submit");
        Assert.AreEqual(HttpStatusCode.OK, submitted.StatusCode);
        var body = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(body.GetProperty("replayed").GetBoolean());
        Assert.AreEqual("Submitted", body.GetProperty("revision").GetProperty("state").GetString());

        // Idempotent replay of the same operation by the same actor; another operation id on a stale version conflicts.
        var replay = await Transition(client, author, id, "Submit", 0, "o-submit");
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.IsTrue((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        var stale = await Transition(client, author, id, "Submit", 0, "o-other");
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);

        // Nobody else may replay the author's operation id.
        var foreignReplay = await Transition(client, reviewer, id, "Submit", 0, "o-submit");
        Assert.AreNotEqual(HttpStatusCode.OK, foreignReplay.StatusCode);

        // Self-approval is denied even for a reviewer who shares the author's subject name.
        var selfReviewer = Sign("synthetic-author", ["reviewer"], [tenant]);
        var self = await Transition(client, selfReviewer, id, "Approve", 1, "o-self");
        Assert.AreEqual(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.AreEqual(LifecycleDenials.SelfApprovalDenied,
            (await self.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Approved is not public: only a publisher may publish; a reviewer and the author may not.
        var approved = await Transition(client, reviewer, id, "Approve", 1, "o-approve");
        Assert.AreEqual(HttpStatusCode.OK, approved.StatusCode);
        var approvedRevision = (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision");
        Assert.AreEqual("Approved", approvedRevision.GetProperty("state").GetString());
        Assert.AreEqual(Digest, approvedRevision.GetProperty("approvedDigest").GetString());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Transition(client, reviewer, id, "Publish", 2, "o-pub-rev")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Transition(client, author, id, "Publish", 2, "o-pub-auth")).StatusCode);

        var published = await Transition(client, publisher, id, "Publish", 2, "o-publish");
        Assert.AreEqual(HttpStatusCode.OK, published.StatusCode);
        Assert.AreEqual("Published",
            (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetProperty("state").GetString());

        var withdrawn = await Transition(client, publisher, id, "Withdraw", 3, "o-withdraw");
        Assert.AreEqual(HttpStatusCode.OK, withdrawn.StatusCode);
        var read = await Send(client, HttpMethod.Get, $"{Base}/{id}", reviewer, null);
        Assert.AreEqual("Withdrawn", (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());

        // Withdrawn is terminal: nothing publishes it again.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Transition(client, publisher, id, "Publish", 4, "o-again")).StatusCode);
    }

    [TestMethod]
    public async Task ADraftWhosePolicyDoesNotPermitPublicDisplayCanNeverBePublished()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();
        var tenant = TenantA.ToString("D");
        var author = Sign("synthetic-author", ["operator"], [tenant]);
        var reviewer = Sign("synthetic-reviewer", ["reviewer"], [tenant]);
        var publisher = Sign("synthetic-publisher", ["publisher"], [tenant]);

        var created = await Send(client, HttpMethod.Post, Base, author,
            new { contentDigest = Digest, policyPermitsPublicDisplay = false });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.AreEqual(HttpStatusCode.OK, (await Transition(client, author, id, "Submit", 0, "o1")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Transition(client, reviewer, id, "Approve", 1, "o2")).StatusCode);
        var publish = await Transition(client, publisher, id, "Publish", 2, "o3");
        Assert.AreEqual(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.AreEqual(LifecycleDenials.PublicDisplayNotPermitted,
            (await publish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task AnotherTenantsRevisionIsIndistinguishableFromAnUnknownOne()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();
        var authorA = Sign("synthetic-author", ["operator"], [TenantA.ToString("D")]);
        var publisherB = Sign("synthetic-publisher", ["publisher"], [TenantB.ToString("D")]);
        var reviewerB = Sign("synthetic-reviewer", ["reviewer"], [TenantB.ToString("D")]);

        var created = await Send(client, HttpMethod.Post, Base, authorA,
            new { contentDigest = Digest, policyPermitsPublicDisplay = true });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var foreign = await Send(client, HttpMethod.Get, $"{Base}/{id}", reviewerB, null);
        var unknown = await Send(client, HttpMethod.Get, $"{Base}/{Guid.NewGuid()}", reviewerB, null);
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());

        foreach (var (bearer, action) in new[] { (publisherB, "Withdraw"), (reviewerB, "Approve") })
        {
            var response = await Transition(client, bearer, id, action, 0, "o-cross-" + action);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, action);
        }

        // The owning tenant's revision is untouched.
        var own = await Send(client, HttpMethod.Get, $"{Base}/{id}", authorA, null);
        var state = await own.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Draft", state.GetProperty("state").GetString());
        Assert.AreEqual(0, state.GetProperty("version").GetInt64());
    }

    private static Task<HttpResponseMessage> Transition(
        HttpClient client, string bearer, Guid id, string action, long expected, string operationId) =>
        Send(client, HttpMethod.Post, $"{Base}/{id}/transitions", bearer,
            new { action, expectedVersion = expected, operationId });

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string bearer, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (method == HttpMethod.Post || method == HttpMethod.Put)
        {
            request.Content = body is null
                ? new StringContent(string.Empty, Encoding.UTF8, "application/json")
                : JsonContent.Create(body);
        }

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
        builder.Database += "_pubrevapi";
        return builder.ConnectionString;
    }

    private static async Task ResetAsync(string connectionString)
    {
        await using var db = new VeritasAtlasDbContext(
            new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private static string Sign(string? name, string[] roles, string[] tenants)
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

    /// <summary>Store double that counts calls and never finds anything.</summary>
    private sealed class CountingStore : ITenantPublicationRevisionStore
    {
        private int _calls;
        public int Calls => _calls;

        public Task<TenantPublicationRevisionModel> CreateDraftAsync(
            Guid tenantId, string authorId, string contentDigest, bool policyPermitsPublicDisplay,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("The store must not be reached.");
        }

        public Task<TenantPublicationRevisionModel?> GetAsync(
            Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TenantPublicationRevisionModel?>(null);
        }

        public Task<PublicationTransitionResult> TransitionAsync(
            Guid tenantId, Guid revisionId, string actorId, LifecycleRole actorRole, LifecycleAction action,
            long expectedVersion, string operationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new PublicationTransitionResult(PublicationTransitionOutcome.NotFound, null, null));
        }

        public Task<VisibilityChangeResult> SetVisibilityAsync(
            Guid tenantId, Guid revisionId, string actorId, LifecycleRole actorRole, PublicVisibilityFactsInput facts,
            long expectedFactsVersion, string operationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new VisibilityChangeResult(VisibilityOutcome.NotFound, null, null));
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantPublicationRevisionStore? store = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0005");
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
