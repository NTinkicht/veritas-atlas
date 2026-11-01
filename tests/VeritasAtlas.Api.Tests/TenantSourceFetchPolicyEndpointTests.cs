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

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-04 slice 5c (#123, decision B): fetch policy HTTP endpoints. Reads
/// are open to any member of the tenant; every write needs exactly the
/// <c>admin</c> role and is refused before the registry otherwise; foreign and
/// unknown sources are indistinguishable. Database-backed tests run against a
/// real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own
/// <c>*_fetchpolicyapi</c> database) and are inconclusive otherwise.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantSourceFetchPolicyEndpointTests
{
    private const string SigningSecret = "fetch-policy-api-test-only-key-not-for-production-01";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string PolicyPath(Guid id) => $"/api/v1/tenant-sources/{id}/fetch-policy";

    private static object Body(
        string version = "2026-10.1",
        string[]? hosts = null,
        string[]? schemes = null,
        int maxRedirects = 3,
        bool httpException = false,
        bool wideningApproved = false,
        string jurisdiction = "dz") => new
        {
            policyVersion = version,
            jurisdiction,
            sourceType = "court_record",
            allowedHosts = hosts ?? ["records.example.dz", "archive.example.dz"],
            allowedSchemes = schemes ?? ["https"],
            allowedPorts = new[] { 443 },
            maxRedirects,
            plaintextHttpException = httpException,
            wideningApproved
        };

    [TestMethod]
    public async Task EveryNonAdminWriterIsRefusedBeforeTheRegistry()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var source = Guid.NewGuid();

        var bearers = new List<(string Label, string Bearer)>
        {
            ("operator", tokens.CreateToken("synthetic-op", "operator", TenantA).Token),
            ("reviewer", tokens.CreateToken("synthetic-rev", "reviewer", TenantA).Token),
            ("publisher", tokens.CreateToken("synthetic-pub", "publisher", TenantA).Token),
            ("analyst", tokens.CreateToken("synthetic-an", "analyst", TenantA).Token),
            ("no role", Sign("synthetic-none", [], [TenantA.ToString("D")])),
            ("admin plus operator", Sign("synthetic-two", ["admin", "operator"], [TenantA.ToString("D")])),
            ("administrator", Sign("synthetic-near", ["administrator"], [TenantA.ToString("D")])),
        };
        foreach (var (label, bearer) in bearers)
        {
            using var put = await Send(client, HttpMethod.Put, PolicyPath(source), bearer,
                Body(wideningApproved: true));
            Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode, label);
            StringAssert.Contains(await put.Content.ReadAsStringAsync(), "admin_required", label);
        }

        Assert.AreEqual(0, registry.Calls, "no registry access for a non-admin writer");

        // Reads are open to any tenant member, whatever the role.
        using var read = await Send(client, HttpMethod.Get, PolicyPath(source), bearers[0].Bearer);
        Assert.AreEqual(HttpStatusCode.NotFound, read.StatusCode);
        Assert.AreEqual(1, registry.Calls);
    }

    [TestMethod]
    public async Task MissingTenantOrActorIsRefusedBeforeTheRegistry()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var source = Guid.NewGuid();

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-admin", "admin").Token,
                     Sign("synthetic-admin", ["admin"], ["not-a-uuid"]),
                     Sign("synthetic-admin", ["admin"], [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var get = await Send(client, HttpMethod.Get, PolicyPath(source), bearer);
            using var put = await Send(client, HttpMethod.Put, PolicyPath(source), bearer, Body());
            Assert.AreEqual(HttpStatusCode.Forbidden, get.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode);
            StringAssert.Contains(await put.Content.ReadAsStringAsync(), "tenant_required");
        }

        foreach (var name in new[] { null, "   ", "a\u0001b" })
        {
            using var put = await Send(client, HttpMethod.Put, PolicyPath(source),
                Sign(name, ["admin"], [TenantA.ToString("D")]), Body());
            Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode, name ?? "<no name>");
            StringAssert.Contains(await put.Content.ReadAsStringAsync(), "actor_required");
        }

        using var anonymous = await client.GetAsync(PolicyPath(source));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, registry.Calls);
    }

    [TestMethod]
    public async Task MalformedBodiesAreRejectedBeforeTheRegistry()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();
        var admin = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-admin", "admin", TenantA).Token;
        var path = PolicyPath(Guid.NewGuid());

        foreach (var json in new[]
                 {
                     "",
                     "null",
                     "{\"policyVersion\":\"2026-10.1\",\"tenantId\":\"" + TenantB + "\"}",
                     "{\"policyVersion\":\"2026-10.1\",\"allowedPorts\":[\"x\"]}",
                     "{\"policyVersion\":\"2026-10.1\",\"padding\":\"" + new string('p', 20_000) + "\"}",
                 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
            using var response = await client.SendAsync(request);
            Assert.IsTrue(
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
                $"{json[..Math.Min(json.Length, 60)]} -> {response.StatusCode}");
        }

        Assert.AreEqual(0, registry.Calls);
    }

    [TestMethod]
    public async Task EveryMissingOrNullPolicyMemberIsA400BeforeTheRegistry()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();
        var admin = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-admin", "admin", TenantA).Token;
        var path = PolicyPath(Guid.NewGuid());
        var full = new Dictionary<string, object?>
        {
            ["policyVersion"] = "2026-10.1",
            ["jurisdiction"] = "dz",
            ["sourceType"] = "court_record",
            ["allowedHosts"] = new[] { "records.example.dz" },
            ["allowedSchemes"] = new[] { "https" },
            ["allowedPorts"] = new[] { 443 },
            ["maxRedirects"] = 3,
        };

        foreach (var member in full.Keys)
        {
            foreach (var mode in new[] { "missing", "null" })
            {
                var body = new Dictionary<string, object?>(full);
                if (mode == "missing") body.Remove(member);
                else body[member] = null;
                using var response = await Send(client, HttpMethod.Put, path, admin, body);
                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"{member} {mode}");
                StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_fetch_policy",
                    $"{member} {mode}");
            }
        }

        Assert.AreEqual(0, registry.Calls, "an incomplete policy never reaches the registry");

        // The complete body does reach it (the double answers 404).
        using var complete = await Send(client, HttpMethod.Put, path, admin, full);
        Assert.AreEqual(HttpStatusCode.NotFound, complete.StatusCode);
        Assert.AreEqual(1, registry.Calls);
    }

    [TestMethod]
    public async Task AdminLifecycleOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var admin = tokens.CreateToken("synthetic-admin", "Admin", TenantA).Token;
        var operatorA = tokens.CreateToken("synthetic-op", "operator", TenantA).Token;
        var source = await CreateSourceAsync(client, admin, "https://records.example.dz/a");

        // No policy yet: same body as an unknown source.
        using (var none = await Send(client, HttpMethod.Get, PolicyPath(source), operatorA))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, none.StatusCode);
        }

        var first = await ProposeAsync(client, admin, source, Body(), HttpStatusCode.OK);
        Assert.IsTrue(first!.Changed);
        Assert.AreEqual(1, first.Policy.Revision);
        Assert.AreEqual(source, first.Policy.SourceId);
        Assert.IsFalse(first.Policy.WideningApproved);
        CollectionAssert.AreEqual(new[] { "records.example.dz", "archive.example.dz" }, first.Policy.AllowedHosts);

        using (var read = await Send(client, HttpMethod.Get, PolicyPath(source), operatorA))
        {
            Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
            var current = (await read.Content.ReadFromJsonAsync<PolicyDto>())!;
            Assert.AreEqual(1, current.Revision);
            Assert.AreEqual("2026-10.1", current.PolicyVersion);
            Assert.AreEqual("dz", current.Jurisdiction);
            Assert.AreEqual("court_record", current.SourceType);
            CollectionAssert.AreEqual(new[] { "https" }, current.AllowedSchemes);
            CollectionAssert.AreEqual(new[] { 443 }, current.AllowedPorts);
            Assert.AreEqual(3, current.MaxRedirects);
        }

        var same = await ProposeAsync(client, admin, source, Body(), HttpStatusCode.OK);
        Assert.IsFalse(same!.Changed);
        Assert.AreEqual(1, same.Policy.Revision);

        var narrowed = await ProposeAsync(client, admin, source,
            Body("2026-10.2", hosts: ["records.example.dz"], maxRedirects: 1), HttpStatusCode.OK);
        Assert.AreEqual(2, narrowed!.Policy.Revision);

        using (var refused = await Send(client, HttpMethod.Put, PolicyPath(source), admin,
                   Body("2026-10.3", hosts: ["records.example.dz", "new.example.dz"], maxRedirects: 1)))
        {
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            var refusal = (await refused.Content.ReadFromJsonAsync<RefusalDto>())!;
            Assert.AreEqual("fetch_policy_refused", refusal.Code);
            CollectionAssert.AreEqual(new[] { "widening_requires_approval" }, refusal.Reasons);
        }

        using (var invalid = await Send(client, HttpMethod.Put, PolicyPath(source), admin,
                   Body("2026-10.3", hosts: ["127.0.0.1"], schemes: ["http"])))
        {
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
            CollectionAssert.AreEqual(new[] { "invalid_allowed_host", "http_without_exception" },
                (await invalid.Content.ReadFromJsonAsync<RefusalDto>())!.Reasons);
        }

        var widened = await ProposeAsync(client, admin, source,
            Body("2026-10.3", hosts: ["records.example.dz", "new.example.dz"], maxRedirects: 1, wideningApproved: true),
            HttpStatusCode.OK);
        Assert.AreEqual(3, widened!.Policy.Revision);
        Assert.IsTrue(widened.Policy.WideningApproved);

        await using var db = new VeritasAtlasDbContext(Options(cs));
        Assert.AreEqual(3, await db.TenantSourceFetchPolicies.CountAsync());
        Assert.AreEqual(3, await db.TenantAuditEvents.CountAsync(e => e.Action.StartsWith("tenant_source.fetch_policy")));
        Assert.AreEqual(3, await db.TenantAuditEvents.CountAsync(e =>
            e.Action.StartsWith("tenant_source.fetch_policy") && e.ActorSubject == "synthetic-admin"));
    }

    [TestMethod]
    public async Task ForeignAndUnknownSourcesAreIndistinguishableOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var adminA = tokens.CreateToken("synthetic-admin-a", "admin", TenantA).Token;
        var adminB = tokens.CreateToken("synthetic-admin-b", "admin", TenantB).Token;

        var bSource = await CreateSourceAsync(client, adminB, "https://records.example.dz/b");
        await ProposeAsync(client, adminB, bSource, Body(), HttpStatusCode.OK);
        var aSource = await CreateSourceAsync(client, adminA, "https://records.example.dz/a");

        using var crossGet = await Send(client, HttpMethod.Get, PolicyPath(bSource), adminA);
        using var unknownGet = await Send(client, HttpMethod.Get, PolicyPath(Guid.NewGuid()), adminA);
        using var noPolicyGet = await Send(client, HttpMethod.Get, PolicyPath(aSource), adminA);
        using var crossPut = await Send(client, HttpMethod.Put, PolicyPath(bSource), adminA,
            Body("2026-10.2", hosts: ["attacker.example.com"], wideningApproved: true));
        using var unknownPut = await Send(client, HttpMethod.Put, PolicyPath(Guid.NewGuid()), adminA, Body());

        var expected = await unknownGet.Content.ReadAsStringAsync();
        foreach (var response in new[] { crossGet, unknownGet, noPolicyGet, crossPut, unknownPut })
        {
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual(expected, await response.Content.ReadAsStringAsync());
            Assert.AreEqual(unknownGet.Content.Headers.ContentType?.ToString(),
                response.Content.Headers.ContentType?.ToString());
        }

        await using var db = new VeritasAtlasDbContext(Options(cs));
        var stored = await db.TenantSourceFetchPolicies.AsNoTracking().SingleAsync();
        Assert.AreEqual(TenantB, stored.TenantId);
        CollectionAssert.AreEqual(new[] { "records.example.dz", "archive.example.dz" }, stored.AllowedHosts.ToArray());
        Assert.AreEqual(0, await db.TenantAuditEvents.CountAsync(e =>
            e.TenantId == TenantA && e.Action.StartsWith("tenant_source.fetch_policy")));
    }

    [TestMethod]
    public async Task ConcurrentAdminWritesKeepRevisionsContiguousOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var admin = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-admin", "admin", TenantA).Token;
        var source = await CreateSourceAsync(client, admin, "https://records.example.dz/race");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await Send(client, HttpMethod.Put, PolicyPath(source), admin, Body());
            return response.StatusCode;
        }));
        Assert.IsTrue(responses.All(s => s is HttpStatusCode.OK or HttpStatusCode.Conflict),
            string.Join(',', responses));
        Assert.IsTrue(responses.Contains(HttpStatusCode.OK));

        await using var db = new VeritasAtlasDbContext(Options(cs));
        var revisions = await db.TenantSourceFetchPolicies.AsNoTracking()
            .Select(x => x.Revision).OrderBy(x => x).ToListAsync();
        CollectionAssert.AreEqual(new[] { 1 }, revisions, "identical concurrent proposals write one revision");
    }

    private sealed record PolicyDto(
        Guid SourceId, int Revision, string PolicyVersion, string Jurisdiction, string SourceType,
        List<string> AllowedHosts, List<string> AllowedSchemes, List<int> AllowedPorts,
        int MaxRedirects, bool PlaintextHttpException, bool WideningApproved, DateTime CreatedAtUtc);

    private sealed record ProposalDto(bool Changed, PolicyDto Policy);

    private sealed record RefusalDto(string Code, string Message, List<string> Reasons);

    private sealed record SourceDto(Guid Id);

    private static async Task<Guid> CreateSourceAsync(HttpClient client, string bearer, string key)
    {
        using var response = await Send(client, HttpMethod.Post, "/api/v1/tenant-sources", bearer,
            new { canonicalKey = key });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SourceDto>())!.Id;
    }

    private static async Task<ProposalDto?> ProposeAsync(
        HttpClient client, string bearer, Guid source, object body, HttpStatusCode expected)
    {
        using var response = await Send(client, HttpMethod.Put, PolicyPath(source), bearer, body);
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<ProposalDto>();
    }

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
        builder.Database += "_fetchpolicyapi";
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

    /// <summary>Registry double that records calls and never finds anything.</summary>
    private sealed class RecordingRegistry : ITenantSourceFetchPolicyRegistry
    {
        private int _calls;
        public int Calls => _calls;

        public Task<SourceFetchPolicyProposalResult> ProposeAsync(
            Guid tenantId, Guid sourceRegistryEntryId, SourceFetchPolicy proposed, bool wideningApproved,
            string actorSubject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new SourceFetchPolicyProposalResult(
                SourceFetchPolicyProposalOutcome.SourceNotFound, null, []));
        }

        public Task<TenantSourceFetchPolicyModel?> GetCurrentAsync(
            Guid tenantId, Guid sourceRegistryEntryId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TenantSourceFetchPolicyModel?>(null);
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantSourceFetchPolicyRegistry? registry = null)
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
                if (registry is not null) services.AddSingleton(registry);
            });
        }
    }
}
