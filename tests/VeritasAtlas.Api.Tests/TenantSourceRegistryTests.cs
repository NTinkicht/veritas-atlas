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
using Microsoft.Extensions.Configuration;
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
/// VER-P0-03 slice 1 (#82, owner decision A in #80): signed tenant claim and
/// tenant-scoped source registry with IDOR denial. Database-backed tests run
/// against a real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (they use
/// their own <c>*_tenant</c> database) and are inconclusive otherwise.
/// </summary>
[TestClass]
public sealed class TenantSourceRegistryTests
{
    private const string SigningSecret = "tenant-registry-test-only-key-not-for-production-0123";
    private const string Path = "/api/v1/tenant-sources";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    [TestMethod]
    public void IssuedTokenCarriesExactlyTheConfiguredTenantAndNeverADefault()
    {
        using var factory = new ApiFactory();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        var withTenant = Read(tokens.CreateToken("synthetic-analyst", "analyst", TenantA).Token);
        Assert.AreEqual(TenantA.ToString("D"), withTenant.Claims.Single(c => c.Type == TenantClaim.Type).Value);

        Assert.IsFalse(Read(tokens.CreateToken("synthetic-analyst", "analyst").Token)
            .Claims.Any(c => c.Type == TenantClaim.Type));
        Assert.IsFalse(Read(tokens.CreateToken("synthetic-analyst", "analyst", Guid.Empty).Token)
            .Claims.Any(c => c.Type == TenantClaim.Type));
    }

    [TestMethod]
    public void TenantResolutionFailsClosedOnMissingMalformedEmptyOrRepeatedClaims()
    {
        Assert.AreEqual(TenantA, TenantClaim.GetTenantId(Principal(TenantA.ToString("D"))));
        Assert.IsNull(TenantClaim.GetTenantId(Principal()));
        Assert.IsNull(TenantClaim.GetTenantId(Principal("not-a-uuid")));
        Assert.IsNull(TenantClaim.GetTenantId(Principal(Guid.Empty.ToString("D"))));
        Assert.IsNull(TenantClaim.GetTenantId(Principal(TenantA.ToString("N"))));
        Assert.IsNull(TenantClaim.GetTenantId(Principal(TenantA.ToString("D"), TenantB.ToString("D"))));
        Assert.IsNull(TenantClaim.GetTenantId(new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(TenantClaim.Type, TenantA.ToString("D")) }))),
            "an unauthenticated identity has no tenant");
    }

    [TestMethod]
    public void IdentitiesCarryOnlyTheirConfiguredTenant()
    {
        Assert.AreEqual(DevUserStore.DevelopmentTenantId,
            new DevUserStore().Validate("operator1", "password123")!.TenantId);

        const string password = "an-independent-staging-secret-1234";
        var configured = Config(new()
        {
            ["Auth:Bootstrap:Username"] = "stagingadmin",
            ["Auth:Bootstrap:Password"] = password,
            [ProductionBootstrapAuthenticator.TenantIdKey] = TenantB.ToString("D")
        });
        ProductionBootstrapAuthenticator.ValidateConfiguration(configured, "Production");
        Assert.AreEqual(TenantB, new ProductionBootstrapAuthenticator(configured).Validate("stagingadmin", password)!.TenantId);

        var unconfigured = Config(new()
        {
            ["Auth:Bootstrap:Username"] = "stagingadmin",
            ["Auth:Bootstrap:Password"] = password
        });
        Assert.IsNull(new ProductionBootstrapAuthenticator(unconfigured).Validate("stagingadmin", password)!.TenantId);

        foreach (var bad in new[] { "not-a-uuid", Guid.Empty.ToString("D"), "" })
        {
            var malformed = Config(new()
            {
                ["Auth:Bootstrap:Username"] = "stagingadmin",
                ["Auth:Bootstrap:Password"] = password,
                [ProductionBootstrapAuthenticator.TenantIdKey] = bad
            });
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ProductionBootstrapAuthenticator.ValidateConfiguration(malformed, "Production"), bad);
        }
    }

    [TestMethod]
    public async Task TokenWithoutValidTenantIsDeniedBeforeTheRegistryIsTouched()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-admin", "admin").Token,
                     Sign("not-a-uuid"),
                     Sign(TenantA.ToString("D"), TenantB.ToString("D"))
                 })
        {
            using var create = await Send(client, HttpMethod.Post, Path, bearer,
                new { canonicalKey = "https://example.test/a", tenantId = TenantA });
            using var get = await Send(client, HttpMethod.Get, $"{Path}/{Guid.NewGuid()}?tenant={TenantA}", bearer);
            using var list = await Send(client, HttpMethod.Get, Path, bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, get.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, list.StatusCode);
            StringAssert.Contains(await list.Content.ReadAsStringAsync(), "tenant_required");
        }

        using var anonymous = await client.GetAsync(Path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, registry.Calls, "no registry access without a valid tenant");
    }

    [TestMethod]
    public async Task TenantIsTakenFromTheSignedClaimNeverFromTheRequest()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();
        var bearer = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-admin", "admin", TenantA).Token;

        using var create = await Send(client, HttpMethod.Post, $"{Path}?tenant={TenantB}", bearer,
            new { canonicalKey = "  https://example.test/a  ", tenantId = TenantB });
        using var blank = await Send(client, HttpMethod.Post, Path, bearer, new { canonicalKey = "   " });
        using var control = await Send(client, HttpMethod.Post, Path, bearer, new { canonicalKey = "a\u0000b" });
        using var oversized = await Send(client, HttpMethod.Post, Path, bearer,
            new { canonicalKey = new string('k', TenantSourceRegistryLimits.MaxCanonicalKeyLength + 1) });

        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, control.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.AreEqual((TenantA, "https://example.test/a", "synthetic-admin"), registry.Created.Single());
    }

    [TestMethod]
    public async Task CrossTenantIdsAreIndistinguishableFromUnknownIdsOverRealPostgres()
    {
        if (TenantDatabase() is not { } connectionString) return;
        await ResetAsync(connectionString);
        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        // Writes need a tenant admin (#125); reads stay open to any tenant member.
        var a = tokens.CreateToken("synthetic-analyst-a", "analyst", TenantA).Token;
        var b = tokens.CreateToken("synthetic-analyst-b", "analyst", TenantB).Token;
        var adminA = tokens.CreateToken("synthetic-admin-a", "admin", TenantA).Token;
        var adminB = tokens.CreateToken("synthetic-admin-b", "admin", TenantB).Token;

        var aEntry = await CreateAsync(client, adminA, "https://example.test/shared");
        var bEntry = await CreateAsync(client, adminB, "https://example.test/shared");
        await CreateAsync(client, adminB, "https://example.test/b-only");

        using var own = await Send(client, HttpMethod.Get, $"{Path}/{aEntry.Id}", a);
        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode);

        using var crossTenant = await Send(client, HttpMethod.Get, $"{Path}/{bEntry.Id}", a);
        using var unknown = await Send(client, HttpMethod.Get, $"{Path}/{Guid.NewGuid()}", a);
        Assert.AreEqual(HttpStatusCode.NotFound, crossTenant.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await crossTenant.Content.ReadAsStringAsync());
        Assert.AreEqual(unknown.Content.Headers.ContentType?.ToString(), crossTenant.Content.Headers.ContentType?.ToString());

        var aList = await ListAsync(client, a);
        var bList = await ListAsync(client, b);
        CollectionAssert.AreEqual(new[] { aEntry.Id }, aList.Select(x => x.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "https://example.test/b-only", "https://example.test/shared" },
            bList.Select(x => x.CanonicalKey).ToArray());

        // Duplicate canonical key: rejected within a tenant, allowed across.
        using var duplicate = await Send(client, HttpMethod.Post, Path, adminA, new { canonicalKey = "https://example.test/shared" });
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.AreEqual(1, (await ListAsync(client, a)).Count);
    }

    [TestMethod]
    public async Task SchemaEnforcesTenantPartitionedKeysUnderConcurrency()
    {
        if (TenantDatabase() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);

        // 8 concurrent creates of one key in one tenant: exactly one row.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantSourceRegistry(db, TimeProvider.System)
                .CreateAsync(TenantA, "https://example.test/race", "synthetic-analyst", CancellationToken.None);
        }));
        Assert.AreEqual(1, results.Count(r => r is not null));

        await using var check = new VeritasAtlasDbContext(options);
        Assert.AreEqual(1, await check.TenantAuditEvents.CountAsync(), "one event for the one winner (#99)");
        var winner = results.Single(r => r is not null)!;
        var registry = new EfTenantSourceRegistry(check, TimeProvider.System);
        Assert.IsNull(await registry.GetAsync(TenantB, winner.Id, CancellationToken.None));
        Assert.AreEqual(0, (await registry.ListAsync(TenantB, CancellationToken.None)).Count);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            registry.ListAsync(Guid.Empty, CancellationToken.None));

        // The same id under another tenant is a distinct key; empty tenant and
        // blank keys are refused by the schema itself.
        await check.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO tenant_source_registry_entries (tenant_id, id, canonical_key, created_at_utc) VALUES ({TenantB}, {winner.Id}, {"https://example.test/race"}, {DateTime.UtcNow})");
        await Assert.ThrowsExactlyAsync<PostgresException>(() => check.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO tenant_source_registry_entries (tenant_id, id, canonical_key, created_at_utc) VALUES ({Guid.Empty}, {Guid.NewGuid()}, {"k"}, {DateTime.UtcNow})"));
        await Assert.ThrowsExactlyAsync<PostgresException>(() => check.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO tenant_source_registry_entries (tenant_id, id, canonical_key, created_at_utc) VALUES ({TenantA}, {Guid.NewGuid()}, {"  "}, {DateTime.UtcNow})"));
        Assert.AreEqual(2, await check.TenantSourceRegistryEntries.CountAsync());
    }

    [TestMethod]
    public async Task TokenWithoutAUsableNameIsRefusedBeforeTheRegistryIsTouched()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();

        foreach (var name in new[] { null, "   ", "a\u0001b", new string('n', TenantAuditEventLimits.MaxSubjectLength + 1) })
        {
            using var create = await Send(client, HttpMethod.Post, Path,
                SignWithName(name, TenantA.ToString("D")), new { canonicalKey = "https://example.test/a" });
            Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode, name ?? "<no name claim>");
            StringAssert.Contains(await create.Content.ReadAsStringAsync(), "actor_required");
        }

        Assert.AreEqual(0, registry.Calls, "no registry access without an audit actor");
    }

    [TestMethod]
    public async Task OnlyASingleAdminRoleMayRegisterASourceAndRefusalsTouchNothing()
    {
        var registry = new RecordingRegistry();
        using var factory = new ApiFactory(registry: registry);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        var refused = new List<(string Label, string Bearer)>();
        foreach (var role in new[] { "operator", "reviewer", "publisher", "analyst", "anonymous" })
        {
            refused.Add((role, tokens.CreateToken($"synthetic-{role}", role, TenantA).Token));
        }
        refused.Add(("no role", SignWithRoles("synthetic-norole", Array.Empty<string>(), TenantA.ToString("D"))));
        refused.Add(("admin+operator", SignWithRoles("synthetic-two", new[] { "admin", "operator" }, TenantA.ToString("D"))));
        refused.Add(("administrator", SignWithRoles("synthetic-near", new[] { "administrator" }, TenantA.ToString("D"))));

        foreach (var (label, bearer) in refused)
        {
            // A valid body and a malformed one both get the role refusal first.
            using var create = await Send(client, HttpMethod.Post, Path, bearer, new { canonicalKey = "https://example.test/a" });
            using var blank = await Send(client, HttpMethod.Post, Path, bearer, new { canonicalKey = "   " });
            Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode, label);
            Assert.AreEqual(HttpStatusCode.Forbidden, blank.StatusCode, label);
            StringAssert.Contains(await create.Content.ReadAsStringAsync(), "admin_required", label);
            StringAssert.Contains(await blank.Content.ReadAsStringAsync(), "admin_required", label);

            // Reads remain open to every authenticated member of the tenant.
            using var list = await Send(client, HttpMethod.Get, Path, bearer);
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, label);
        }
        Assert.AreEqual(0, registry.Created.Count, "no registration without a single admin role");

        foreach (var (label, bearer) in new[]
                 {
                     ("issued admin", tokens.CreateToken("synthetic-admin", "admin", TenantA).Token),
                     ("upper-case ADMIN", SignWithRoles("synthetic-upper", new[] { "ADMIN" }, TenantA.ToString("D"))),
                 })
        {
            using var create = await Send(client, HttpMethod.Post, Path, bearer,
                new { canonicalKey = $"https://example.test/{Guid.NewGuid():N}" });
            Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, label);
        }
        Assert.AreEqual(2, registry.Created.Count);
    }

    [TestMethod]
    public async Task NonAdminRegistrationWritesNoSourceAndNoAuditEventOverRealPostgres()
    {
        if (TenantDatabase() is not { } connectionString) return;
        await ResetAsync(connectionString);
        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        foreach (var role in new[] { "operator", "reviewer", "publisher", "analyst" })
        {
            using var create = await Send(client, HttpMethod.Post, Path,
                tokens.CreateToken($"synthetic-{role}", role, TenantA).Token,
                new { canonicalKey = "https://example.test/refused" });
            Assert.AreEqual(HttpStatusCode.Forbidden, create.StatusCode, role);
        }

        await using (var check = new VeritasAtlasDbContext(Options(connectionString)))
        {
            Assert.AreEqual(0, await check.TenantSourceRegistryEntries.CountAsync());
            Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync());
        }

        var admin = tokens.CreateToken("synthetic-admin", "admin", TenantA).Token;
        var created = await CreateAsync(client, admin, "https://example.test/refused");
        await using var after = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(created.Id, (await after.TenantSourceRegistryEntries.AsNoTracking().SingleAsync()).Id);
        Assert.AreEqual("synthetic-admin", (await after.TenantAuditEvents.AsNoTracking().SingleAsync()).ActorSubject);
    }

    [TestMethod]
    public async Task RegistrationWritesExactlyOneAuditEventInTheSameTenant()
    {
        if (TenantDatabase() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var registry = new EfTenantSourceRegistry(db, TimeProvider.System);

        var created = await registry.CreateAsync(TenantA, "https://example.test/a", "synthetic-analyst", CancellationToken.None);
        Assert.IsNotNull(created);
        Assert.IsNull(await registry.CreateAsync(TenantA, "https://example.test/a", "synthetic-other", CancellationToken.None),
            "duplicate key");

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        var audit = await check.TenantAuditEvents.AsNoTracking().SingleAsync();
        Assert.AreEqual(TenantA, audit.TenantId);
        Assert.AreEqual($"tenant_source.registered:{created!.Id:D}", audit.EventKey);
        Assert.AreEqual(EfTenantSourceRegistry.RegisteredAction, audit.Action);
        Assert.AreEqual("tenant_source", audit.EntityType);
        Assert.AreEqual(created.Id.ToString("D"), audit.EntityId);
        Assert.AreEqual("synthetic-analyst", audit.ActorSubject);
        Assert.AreEqual(created.CreatedAtUtc, DateTime.SpecifyKind(audit.OccurredAtUtc, DateTimeKind.Utc));
        Assert.AreEqual(created.CreatedAtUtc, (await registry.GetAsync(TenantA, created.Id, CancellationToken.None))!.CreatedAtUtc,
            "round-trip-stable instant");
        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync(e => e.TenantId == TenantB));
    }

    [TestMethod]
    public async Task FailedAuditWriteRollsBackTheRegistration()
    {
        if (TenantDatabase() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        // Test-only fault in this per-test database: every audit insert fails.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION test_refuse_audit() RETURNS trigger LANGUAGE plpgsql AS "
            + "$$BEGIN RAISE EXCEPTION 'audit unavailable' USING ERRCODE = 'P0001'; END$$");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER test_refuse_audit BEFORE INSERT ON tenant_audit_events "
            + "FOR EACH ROW EXECUTE FUNCTION test_refuse_audit()");

        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
            new EfTenantSourceRegistry(db, TimeProvider.System)
                .CreateAsync(TenantA, "https://example.test/a", "synthetic-analyst", CancellationToken.None));
        Assert.AreEqual("P0001", (error.InnerException as PostgresException)?.SqlState);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(0, await check.TenantSourceRegistryEntries.CountAsync());
        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task InvalidActorIsRefusedBeforeTheDatabaseIsTouched()
    {
        // No reachable server: a refusal must happen before any connection.
        var options = new DbContextOptionsBuilder<VeritasAtlasDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1").Options;
        await using var db = new VeritasAtlasDbContext(options);
        var registry = new EfTenantSourceRegistry(db, TimeProvider.System);

        foreach (var actor in new[] { "", "   ", "a\u0000b", "a\u007fb", "a\u0085b",
                     new string('a', TenantAuditEventLimits.MaxSubjectLength + 1) })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                registry.CreateAsync(TenantA, "https://example.test/a", actor, CancellationToken.None));
        }
    }

    private sealed record Entry(Guid Id, string CanonicalKey, DateTime CreatedAtUtc);

    private sealed record EntryList(List<Entry> Items);

    private static async Task<Entry> CreateAsync(HttpClient client, string bearer, string key)
    {
        using var response = await Send(client, HttpMethod.Post, Path, bearer, new { canonicalKey = key });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Entry>())!;
    }

    private static async Task<List<Entry>> ListAsync(HttpClient client, string bearer)
    {
        using var response = await Send(client, HttpMethod.Get, Path, bearer);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EntryList>())!.Items;
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string bearer, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static string? TenantDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_tenant";
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

    private static JwtSecurityToken Read(string bearer) => new JwtSecurityTokenHandler().ReadJwtToken(bearer);

    private static ClaimsPrincipal Principal(params string[] tenants) =>
        new(new ClaimsIdentity(
            tenants.Select(t => new Claim(TenantClaim.Type, t)).Append(new Claim(ClaimTypes.Name, "synthetic")),
            "Bearer"));

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string Sign(params string[] tenants) => SignWithName("synthetic-forged", tenants);

    private static string SignWithName(string? name, params string[] tenants) =>
        SignWithRoles(name, new[] { "admin" }, tenants);

    private static string SignWithRoles(string? name, string[] roles, params string[] tenants)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
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

    /// <summary>Registry double that records calls and never answers reads.</summary>
    private sealed class RecordingRegistry : ITenantSourceRegistry
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid, string, string)> Created { get; } = new();

        public Task<TenantSourceRegistryEntryModel?> CreateAsync(
            Guid tenantId, string canonicalKey, string actorSubject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            lock (Created) Created.Add((tenantId, canonicalKey, actorSubject));
            return Task.FromResult<TenantSourceRegistryEntryModel?>(
                new TenantSourceRegistryEntryModel(Guid.NewGuid(), canonicalKey, DateTime.UtcNow));
        }

        public Task<TenantSourceRegistryEntryModel?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TenantSourceRegistryEntryModel?>(null);
        }

        public Task<IReadOnlyList<TenantSourceRegistryEntryModel>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantSourceRegistryEntryModel>>(Array.Empty<TenantSourceRegistryEntryModel>());
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantSourceRegistry? registry = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0004");
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
