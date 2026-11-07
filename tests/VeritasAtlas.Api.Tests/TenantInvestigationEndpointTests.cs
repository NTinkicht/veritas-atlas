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
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-10 slice 1 (#208): tenant investigation HTTP API. Refusals are proven
/// before the store is touched; creation, replay, conflict, audit and cross-tenant
/// denial run end to end against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c>
/// is set (own <c>*_invapi</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationEndpointTests
{
    private const string SigningSecret = "investigation-api-test-only-key-not-for-production-001";
    private const string Path = "/api/v1/tenant-investigations";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    [TestMethod]
    public async Task RefusedCallersNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        var a = TenantA.ToString("D");

        var bearers = new (string Label, string Bearer)[]
        {
            ("no tenant", Sign("synthetic-a", ["analyst"], [])),
            ("bad tenant", Sign("synthetic-a", ["analyst"], ["not-a-uuid"])),
            ("two tenants", Sign("synthetic-a", ["analyst"], [a, TenantB.ToString("D")])),
            ("no role", Sign("synthetic-a", [], [a])),
            ("unknown role", Sign("synthetic-a", ["anonymous"], [a])),
            ("two roles", Sign("synthetic-a", ["analyst", "reviewer"], [a])),
            ("no name", Sign(null, ["analyst"], [a])),
            ("control name", Sign("a\u0001b", ["analyst"], [a])),
        };
        foreach (var (label, bearer) in bearers)
        {
            using var create = await Send(client, HttpMethod.Post, Path, bearer, new { creationKey = "k1", title = "T" });
            Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode, label);
            if (label is not ("no name" or "control name"))
            {
                using var get = await Send(client, HttpMethod.Get, $"{Path}/{id}", bearer);
                using var list = await Send(client, HttpMethod.Get, Path, bearer);
                Assert.AreEqual(HttpStatusCode.Forbidden, get.StatusCode, label);
                Assert.AreEqual(HttpStatusCode.Forbidden, list.StatusCode, label);
            }
        }

        // Only an author creates; other known roles may read but not create.
        foreach (var role in new[] { "reviewer", "publisher", "admin" })
        {
            var bearer = Sign($"synthetic-{role}", [role], [a]);
            using var create = await Send(client, HttpMethod.Post, Path, bearer, new { creationKey = "k1", title = "T" });
            using var blank = await Send(client, HttpMethod.Post, Path, bearer, new { creationKey = " ", title = " " });
            Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode, role);
            Assert.AreEqual(HttpStatusCode.Forbidden, blank.StatusCode, role);
            StringAssert.Contains(await blank.Content.ReadAsStringAsync(), "author_required", role);
        }

        using var anonymous = await client.GetAsync(Path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, store.Calls, "a refused caller must never reach the store");
    }

    [TestMethod]
    public async Task TenantComesFromTheClaimAndInvalidInputIsRejectedBeforeTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var bearer = Sign("synthetic-analyst", ["analyst"], [TenantA.ToString("D")]);

        foreach (var (key, title) in new (string?, string?)[]
                 {
                     (null, "T"), ("   ", "T"), ("a\u0000b", "T"), (new string('k', TenantInvestigationLimits.MaxCreationKeyLength + 1), "T"),
                     ("k", null), ("k", "  "), ("k", " lead"), ("k", "trail "), ("k", "a\nb"),
                     ("k", new string('t', TenantInvestigationLimits.MaxTitleLength + 1)),
                 })
        {
            using var response = await Send(client, HttpMethod.Post, Path, bearer, new { creationKey = key, title });
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"{key}/{title}");
        }
        using var noBody = await Send(client, HttpMethod.Post, Path, bearer);
        Assert.IsTrue(noBody.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType);
        Assert.AreEqual(0, store.Calls);

        using var ok = await Send(client, HttpMethod.Post, $"{Path}?tenant={TenantB}", bearer,
            new { creationKey = "k1", title = "Valid", tenantId = TenantB });
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, "k1", "Valid", "synthetic-analyst"), store.Created.Single());
    }

    [TestMethod]
    public async Task CreateReplayConflictAndCrossTenantDenialOverRealPostgres()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();
        var analystA = Sign("synthetic-analyst-a", ["analyst"], [TenantA.ToString("D")]);
        var reviewerA = Sign("synthetic-reviewer-a", ["reviewer"], [TenantA.ToString("D")]);
        var operatorB = Sign("synthetic-operator-b", ["operator"], [TenantB.ToString("D")]);

        using var created = await Send(client, HttpMethod.Post, Path, analystA, new { creationKey = "key-1", title = "Case one" });
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var first = (await created.Content.ReadFromJsonAsync<Investigation>())!;
        Assert.AreEqual("synthetic-analyst-a", first.CreatedBySubject);

        using var replay = await Send(client, HttpMethod.Post, Path, analystA, new { creationKey = "key-1", title = "Case one" });
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(first, await replay.Content.ReadFromJsonAsync<Investigation>());

        using var conflict = await Send(client, HttpMethod.Post, Path, analystA, new { creationKey = "key-1", title = "Other" });
        Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        StringAssert.Contains(await conflict.Content.ReadAsStringAsync(), "creation_key_conflict");

        // The same key in another tenant is a distinct investigation.
        using var b = await Send(client, HttpMethod.Post, Path, operatorB, new { creationKey = "key-1", title = "Case one" });
        Assert.AreEqual(HttpStatusCode.Created, b.StatusCode);
        var bInv = (await b.Content.ReadFromJsonAsync<Investigation>())!;
        Assert.AreNotEqual(first.Id, bInv.Id);

        // A reviewer in the same tenant may read.
        using var read = await Send(client, HttpMethod.Get, $"{Path}/{first.Id}", reviewerA);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        Assert.AreEqual(first, await read.Content.ReadFromJsonAsync<Investigation>());

        using var cross = await Send(client, HttpMethod.Get, $"{Path}/{bInv.Id}", analystA);
        using var unknown = await Send(client, HttpMethod.Get, $"{Path}/{Guid.NewGuid()}", analystA);
        Assert.AreEqual(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await cross.Content.ReadAsStringAsync());
        Assert.AreEqual(unknown.Content.Headers.ContentType?.ToString(), cross.Content.Headers.ContentType?.ToString());

        using var listA = await Send(client, HttpMethod.Get, Path, analystA);
        var items = (await listA.Content.ReadFromJsonAsync<InvestigationList>())!.Items;
        CollectionAssert.AreEqual(new[] { first.Id }, items.Select(x => x.Id).ToArray());

        // Exactly one audit event per created investigation, in its own tenant.
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var audits = await db.TenantAuditEvents.AsNoTracking().ToListAsync();
        Assert.AreEqual(2, audits.Count, "replay and conflict write no event");
        Assert.AreEqual("synthetic-analyst-a", audits.Single(e => e.TenantId == TenantA).ActorSubject);
        Assert.AreEqual(first.Id.ToString("D"), audits.Single(e => e.TenantId == TenantA).EntityId);
        Assert.AreEqual(bInv.Id.ToString("D"), audits.Single(e => e.TenantId == TenantB).EntityId);
    }

    private sealed record Investigation(Guid Id, string CreationKey, string Title, string CreatedBySubject, DateTime CreatedAtUtc);

    private sealed record InvestigationList(List<Investigation> Items);

    private static async Task<HttpResponseMessage> Send(
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
        builder.Database += "_invapi";
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

    /// <summary>Store double that counts calls and records accepted creations.</summary>
    private sealed class CountingStore : ITenantInvestigationStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, string Key, string Title, string Subject)> Created { get; } = [];

        public Task<InvestigationCreateResult> CreateAsync(
            Guid tenantId, InvestigationInput input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Created.Add((tenantId, input.CreationKey, input.Title, input.CreatedBySubject));
            var model = new TenantInvestigationModel(
                Guid.NewGuid(), input.CreationKey, input.Title, input.CreatedBySubject, DateTime.UtcNow);
            return Task.FromResult(new InvestigationCreateResult(InvestigationCreateOutcome.Created, model));
        }

        public Task<TenantInvestigationModel?> GetAsync(
            Guid tenantId, Guid investigationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TenantInvestigationModel?>(null);
        }

        public Task<IReadOnlyList<TenantInvestigationModel>> ListAsync(
            Guid tenantId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantInvestigationModel>>([]);
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantInvestigationStore? store = null)
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
