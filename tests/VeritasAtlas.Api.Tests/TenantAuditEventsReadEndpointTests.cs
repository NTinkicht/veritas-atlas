using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-18 (#368): admin read of the tenant audit ledger. SYNTHETIC data only. Role, filter and
/// tenant-claim tests use a recording store double; database-backed tests need
/// <c>VERITAS_TEST_POSTGRES</c> (own <c>*_auditeventsapi</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantAuditEventsReadEndpointTests
{
    private const string SigningSecret = "audit-events-api-test-only-key-not-for-production-0368";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private const string Path = "/api/v1/tenant-audit-events";

    [TestMethod]
    public async Task ListRefusesEveryNonAdminAndBadCallerBeforeTheStore()
    {
        var store = new RecordingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = new[] { TenantA.ToString("D") };

        foreach (var (label, bearer, code) in new List<(string, string, string)>
                 {
                     ("operator", tokens.CreateToken("synthetic-op", "operator", TenantA).Token, "admin_required"),
                     ("analyst", tokens.CreateToken("synthetic-an", "analyst", TenantA).Token, "admin_required"),
                     ("reviewer", tokens.CreateToken("synthetic-rev", "reviewer", TenantA).Token, "admin_required"),
                     ("publisher", tokens.CreateToken("synthetic-pub", "publisher", TenantA).Token, "admin_required"),
                     ("no role", Sign("synthetic-none", [], tenant), ""),
                     ("admin plus operator", Sign("synthetic-two", ["admin", "operator"], tenant), "admin_required"),
                     ("admin without tenant", tokens.CreateToken("synthetic-admin", "admin").Token, "tenant_required"),
                     ("admin without name", Sign(null, ["admin"], tenant), "actor_required"),
                 })
        {
            using var response = await Get(client, Path, bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, label);
            if (code.Length > 0) StringAssert.Contains(await response.Content.ReadAsStringAsync(), code, label);
        }

        using var anonymous = await client.GetAsync(Path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, store.Calls, "no store access for a refused caller");

        // Positive control: an admin of the tenant reaches the store with the claim tenant and the server cap.
        using var admin = await Get(client, Path + "?entityType=investigation&entityId=i-1",
            Sign("synthetic-admin", ["admin"], tenant));
        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode);
        Assert.AreEqual(1, store.Calls);
        Assert.AreEqual((TenantA, "investigation", "i-1", 200), store.LastList);

        using var write = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", Sign("synthetic-admin", ["admin"], tenant)) }
        });
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, write.StatusCode, "read-only: no write verb");
    }

    [TestMethod]
    public async Task InvalidFiltersAre400AndNeverReachTheStore()
    {
        var store = new RecordingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var admin = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        foreach (var (path, code) in new[]
                 {
                     (Path + "?entityId=i-1", "entity_type_required"),
                     (Path + "?entityType=a%0Ab", "entity_type_invalid"),
                     (Path + "?entityType=" + new string('t', 65), "entity_type_invalid"),
                     (Path + "?entityType=investigation&entityId=x%09y", "entity_id_invalid"),
                     (Path + "?entityType=investigation&entityId=" + new string('i', 201), "entity_id_invalid"),
                 })
        {
            using var response = await Get(client, path, admin);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, path);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), code, path);
        }

        Assert.AreEqual(0, store.Calls);
    }

    [TestMethod]
    public async Task ListReturnsOnlyOwnTenantEventsInOrderWithExactShapeAndNoEventKey()
    {
        var cs = Database();
        if (cs is null) return;
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        await using (var seed = new VeritasAtlasDbContext(Options(cs)))
        {
            var store = new EfTenantAuditEventStore(seed, TimeProvider.System);
            await store.AppendAsync(TenantA, new AuditEventInput("secret-key-1", "investigation.created", "investigation", "i-1", "synthetic-seeder"), CancellationToken.None);
            await store.AppendAsync(TenantA, new AuditEventInput("secret-key-2", "evidence.collected", "investigation", "i-1", "synthetic-seeder"), CancellationToken.None);
            await store.AppendAsync(TenantA, new AuditEventInput("secret-key-3", "raw.retention.set", "raw_retrieval", "r-9", "synthetic-seeder"), CancellationToken.None);
            await store.AppendAsync(TenantB, new AuditEventInput("secret-key-4", "investigation.created", "investigation", "i-1", "synthetic-other-tenant"), CancellationToken.None);
        }

        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        using var all = await Get(client, Path, adminA);
        var text = await all.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, all.StatusCode, text);
        var rows = System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { "investigation.created", "evidence.collected", "raw.retention.set" },
            rows.Select(e => e.GetProperty("action").GetString()).ToArray(), "own tenant only, oldest first");
        CollectionAssert.AreEquivalent(
            new[] { "id", "action", "entityType", "entityId", "actorSubject", "occurredAtUtc" },
            rows[0].EnumerateObject().Select(p => p.Name).ToArray(), "exact response shape");
        StringAssert.DoesNotMatch(text, new Regex("secret-key|eventKey|synthetic-other-tenant"), "no event key, no other tenant");

        using var byEntity = await Get(client, Path + "?entityType=raw_retrieval&entityId=r-9", adminA);
        var filtered = System.Text.Json.JsonDocument.Parse(await byEntity.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToArray();
        Assert.AreEqual(1, filtered.Length);
        Assert.AreEqual("raw.retention.set", filtered[0].GetProperty("action").GetString());

        using var foreignView = await Get(client, Path + "?entityType=raw_retrieval&entityId=r-9", adminB);
        Assert.AreEqual("[]", await foreignView.Content.ReadAsStringAsync(), "another tenant sees nothing of this entity");
        using var foreignAll = await Get(client, Path, adminB);
        StringAssert.Contains(await foreignAll.Content.ReadAsStringAsync(), "synthetic-other-tenant");
    }

    [TestMethod]
    public async Task ListRecentKeepsTheNewestRowsOldestFirstAndRejectsBadLimits()
    {
        var cs = Database();
        if (cs is null) return;
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        await using var db = new VeritasAtlasDbContext(Options(cs));
        var store = new EfTenantAuditEventStore(db, TimeProvider.System);
        for (var i = 0; i < 5; i++)
        {
            await store.AppendAsync(TenantA, new AuditEventInput($"cap-{i}", "investigation.created", "investigation", $"cap-{i}", "synthetic-seeder"), CancellationToken.None);
            await Task.Delay(5);
        }

        var top = await store.ListRecentAsync(TenantA, null, null, 2, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "cap-3", "cap-4" }, top.Select(e => e.EntityId).ToArray(), "newest two, oldest first");
        Assert.AreEqual(5, (await store.ListRecentAsync(TenantA, null, null, 200, CancellationToken.None)).Count,
            "positive control: a larger limit returns every row");
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            store.ListRecentAsync(TenantA, null, null, 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.ListRecentAsync(Guid.Empty, null, null, 1, CancellationToken.None));
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client.SendAsync(request);
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
        builder.Database += "_auditeventsapi";
        return builder.ConnectionString;
    }

    private static DbContextOptions<VeritasAtlasDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options;

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

    private sealed class RecordingStore : ITenantAuditEventStore
    {
        private int _calls;
        public int Calls => _calls;
        public (Guid Tenant, string? Type, string? Id, int Limit)? LastList { get; private set; }

        public Task<IReadOnlyList<TenantAuditEventModel>> ListRecentAsync(
            Guid tenantId, string? entityType, string? entityId, int limit, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastList = (tenantId, entityType, entityId, limit);
            return Task.FromResult<IReadOnlyList<TenantAuditEventModel>>([]);
        }

        public Task<IReadOnlyList<TenantAuditEventModel>> ListAsync(
            Guid tenantId, string? entityType, string? entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuditEventAppendResult> AppendAsync(
            Guid tenantId, AuditEventInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantAuditEventStore? store = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0368");
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
