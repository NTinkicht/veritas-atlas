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
/// VER-P0-06 (#280): admin list of fetch attempts for a source. SYNTHETIC data only. Role and
/// tenant tests use a recording ledger double; database-backed tests need <c>VERITAS_TEST_POSTGRES</c>
/// (own <c>*_fetchattemptsapi</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantFetchAttemptsReadEndpointTests
{
    private const string SigningSecret = "fetch-attempts-api-test-only-key-not-for-production-0280";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private const string Doc = "<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Synthetic section one.</P1></Legislation>";

    private static string ListPath(Guid? sourceId) =>
        "/api/v1/tenant-fetch-attempts" + (sourceId is null ? "" : $"?sourceId={sourceId}");

    [TestMethod]
    public async Task ListRefusesEveryNonAdminAndBadCallerBeforeTheLedger()
    {
        var ledger = new RecordingLedger();
        using var factory = new ApiFactory(ledger: ledger);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = new[] { TenantA.ToString("D") };
        var source = Guid.NewGuid();

        foreach (var (label, bearer) in new List<(string, string)>
                 {
                     ("operator", tokens.CreateToken("synthetic-op", "operator", TenantA).Token),
                     ("analyst", tokens.CreateToken("synthetic-an", "analyst", TenantA).Token),
                     ("reviewer", tokens.CreateToken("synthetic-rev", "reviewer", TenantA).Token),
                     ("publisher", tokens.CreateToken("synthetic-pub", "publisher", TenantA).Token),
                     ("no role", Sign("synthetic-none", [], tenant)),
                     ("admin plus operator", Sign("synthetic-two", ["admin", "operator"], tenant)),
                     ("admin without tenant", tokens.CreateToken("synthetic-admin", "admin").Token),
                     ("admin without name", Sign(null, ["admin"], tenant)),
                 })
        {
            using var response = await Get(client, ListPath(source), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, label);
        }

        using var anonymous = await client.GetAsync(ListPath(source));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, ledger.Calls, "no ledger access for a refused caller");

        // Positive control: an admin of the tenant reaches the ledger with the claim tenant and the server cap.
        using var admin = await Get(client, ListPath(source), Sign("synthetic-admin", ["admin"], tenant));
        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode);
        Assert.AreEqual(1, ledger.Calls);
        Assert.AreEqual((TenantA, source, 100), ledger.LastList);
    }

    [TestMethod]
    public async Task ListWithoutOrEmptySourceIdIs400AndNeverReachesTheLedger()
    {
        var ledger = new RecordingLedger();
        using var factory = new ApiFactory(ledger: ledger);
        using var client = factory.CreateClient();
        var admin = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        foreach (var path in new[] { ListPath(null), ListPath(Guid.Empty), "/api/v1/tenant-fetch-attempts?sourceId=nope" })
        {
            using var response = await Get(client, path, admin);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, path);
        }

        Assert.AreEqual(0, ledger.Calls);
    }

    [TestMethod]
    public async Task ListReturnsOnlyOwnTenantSourceAttemptsNewestFirstWithExactShape()
    {
        var cs = Database();
        if (cs is null) return;
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        Guid sourceA, otherSourceA, retrieval, failed, succeeded;
        await using (var seed = new VeritasAtlasDbContext(Options(cs)))
        {
            var registry = new EfTenantSourceRegistry(seed, TimeProvider.System);
            var raw = new EfTenantRawRetrievalLedger(seed, TimeProvider.System);
            var attempts = new EfTenantFetchAttemptLedger(seed, TimeProvider.System);
            sourceA = (await registry.CreateAsync(TenantA, "https://example.test/f1", "synthetic-seeder", CancellationToken.None))!.Id;
            otherSourceA = (await registry.CreateAsync(TenantA, "https://example.test/f2", "synthetic-seeder", CancellationToken.None))!.Id;
            retrieval = (await raw.RecordAsync(TenantA,
                new RawRetrievalInput(sourceA, "raw-key-1", Uri, At.AddMinutes(5), Encoding.UTF8.GetBytes(Doc)),
                "synthetic-seeder", CancellationToken.None)).Record!.Id;
            failed = (await attempts.RecordAsync(TenantA,
                new FetchAttemptInput(sourceA, "secret-attempt-key-1", Uri, At, FetchAttemptOutcome.Failed,
                    FetchFailureClass.HttpStatus, 503, null),
                "synthetic-seeder", CancellationToken.None)).Attempt!.Id;
            succeeded = (await attempts.RecordAsync(TenantA,
                new FetchAttemptInput(sourceA, "secret-attempt-key-2", Uri, At.AddMinutes(5), FetchAttemptOutcome.Succeeded,
                    null, 200, retrieval),
                "synthetic-seeder", CancellationToken.None)).Attempt!.Id;
            await attempts.RecordAsync(TenantA,
                new FetchAttemptInput(otherSourceA, "secret-attempt-key-3", Uri, At.AddHours(1), FetchAttemptOutcome.Failed,
                    FetchFailureClass.Timeout, null, null),
                "synthetic-seeder", CancellationToken.None);
        }

        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        using var own = await Get(client, ListPath(sourceA), adminA);
        var text = await own.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode, text);
        var rows = System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { succeeded, failed }, rows.Select(e => e.GetProperty("id").GetGuid()).ToArray(),
            "newest first, only the requested source");
        CollectionAssert.AreEquivalent(
            new[] { "id", "sourceEntryId", "requestedUri", "attemptedAtUtc", "outcome", "failureClass", "httpStatus", "rawRetrievalId", "createdAtUtc" },
            rows[0].EnumerateObject().Select(p => p.Name).ToArray(), "exact response shape");
        Assert.AreEqual("succeeded", rows[0].GetProperty("outcome").GetString());
        Assert.AreEqual(System.Text.Json.JsonValueKind.Null, rows[0].GetProperty("failureClass").ValueKind);
        Assert.AreEqual(retrieval, rows[0].GetProperty("rawRetrievalId").GetGuid());
        Assert.AreEqual("failed", rows[1].GetProperty("outcome").GetString());
        Assert.AreEqual("http_status", rows[1].GetProperty("failureClass").GetString());
        Assert.AreEqual(503, rows[1].GetProperty("httpStatus").GetInt32());
        StringAssert.DoesNotMatch(text, new Regex("ev1:|Synthetic section|secret-attempt-key|attemptKey|rawBytes"),
            "metadata only");

        using var foreign = await Get(client, ListPath(sourceA), adminB);
        using var unknown = await Get(client, ListPath(Guid.NewGuid()), adminA);
        Assert.AreEqual(HttpStatusCode.OK, foreign.StatusCode);
        Assert.AreEqual("[]", await foreign.Content.ReadAsStringAsync(), "another tenant sees nothing");
        Assert.AreEqual("[]", await unknown.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task LedgerListIsCappedToTheNewestRows()
    {
        var cs = Database();
        if (cs is null) return;
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        await using var db = new VeritasAtlasDbContext(Options(cs));
        var source = (await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(TenantA, "https://example.test/cap", "synthetic-seeder", CancellationToken.None))!.Id;
        var ledger = new EfTenantFetchAttemptLedger(db, TimeProvider.System);
        for (var i = 0; i < 5; i++)
        {
            await ledger.RecordAsync(TenantA,
                new FetchAttemptInput(source, $"cap-{i}", Uri, At.AddMinutes(i), FetchAttemptOutcome.Failed,
                    FetchFailureClass.Network, null, null),
                "synthetic-seeder", CancellationToken.None);
        }

        var top = await ledger.ListRecentForSourceAsync(TenantA, source, 2, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { At.AddMinutes(4), At.AddMinutes(3) }, top.Select(a => a.AttemptedAtUtc).ToArray());
        Assert.AreEqual(5, (await ledger.ListRecentForSourceAsync(TenantA, source, 100, CancellationToken.None)).Count,
            "positive control: a larger limit returns every row");
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            ledger.ListRecentForSourceAsync(TenantA, source, 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ledger.ListRecentForSourceAsync(Guid.Empty, source, 1, CancellationToken.None));
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
        builder.Database += "_fetchattemptsapi";
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

    private sealed class RecordingLedger : ITenantFetchAttemptLedger
    {
        private int _calls;
        public int Calls => _calls;
        public (Guid Tenant, Guid Source, int Limit)? LastList { get; private set; }

        public Task<IReadOnlyList<TenantFetchAttemptModel>> ListRecentForSourceAsync(
            Guid tenantId, Guid sourceEntryId, int limit, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastList = (tenantId, sourceEntryId, limit);
            return Task.FromResult<IReadOnlyList<TenantFetchAttemptModel>>([]);
        }

        public Task<IReadOnlyList<TenantFetchAttemptModel>> ListForSourceAsync(
            Guid tenantId, Guid sourceEntryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FetchAttemptRecordResult> RecordAsync(
            Guid tenantId, FetchAttemptInput input, string actorSubject, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantFetchAttemptLedger? ledger = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0280");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                connectionString ?? "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                if (ledger is not null) services.AddSingleton(ledger);
            });
        }
    }
}
