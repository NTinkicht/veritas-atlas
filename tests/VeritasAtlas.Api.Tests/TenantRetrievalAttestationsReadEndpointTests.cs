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
/// VER-P0-06 (#276): admin list of the attestations of one raw retrieval. SYNTHETIC data only. Role and
/// tenant tests use a recording ledger double; database-backed tests need <c>VERITAS_TEST_POSTGRES</c>
/// (own <c>*_attestreadapi</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantRetrievalAttestationsReadEndpointTests
{
    private const string SigningSecret = "attest-read-api-test-only-key-not-for-production-0276";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private const string Doc = "<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Synthetic section one.</P1></Legislation>";

    private static string Path(Guid id) => $"/api/v1/tenant-raw-retrievals/{id}/attestations";

    [TestMethod]
    public async Task EveryNonAdminCallerIsRefusedBeforeTheLedger()
    {
        var ledger = new RecordingLedgers();
        using var factory = new ApiFactory(ledger: ledger);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = new[] { TenantA.ToString("D") };

        var bearers = new List<(string Label, string Bearer)>
        {
            ("operator", tokens.CreateToken("synthetic-op", "operator", TenantA).Token),
            ("reviewer", tokens.CreateToken("synthetic-rev", "reviewer", TenantA).Token),
            ("publisher", tokens.CreateToken("synthetic-pub", "publisher", TenantA).Token),
            ("analyst", tokens.CreateToken("synthetic-an", "analyst", TenantA).Token),
            ("no role", Sign("synthetic-none", [], tenant)),
            ("admin plus operator", Sign("synthetic-two", ["admin", "operator"], tenant)),
        };
        foreach (var (label, bearer) in bearers)
        {
            using var response = await Get(client, Path(Guid.NewGuid()), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, label);
        }

        Assert.AreEqual(0, ledger.Calls, "no ledger access for a non-admin caller");

        // Positive control: an admin of the tenant reaches the ledger.
        using var admin = await Get(client, Path(Guid.NewGuid()), Sign("synthetic-admin", ["admin"], tenant));
        Assert.AreEqual(1, ledger.Calls);
        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode);
    }

    [TestMethod]
    public async Task MissingTenantActorOrAnonymousIsRefusedBeforeTheLedger()
    {
        var ledger = new RecordingLedgers();
        using var factory = new ApiFactory(ledger: ledger);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-admin", "admin").Token,
                     Sign("synthetic-admin", ["admin"], ["not-a-uuid"]),
                     Sign("synthetic-admin", ["admin"], [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var response = await Get(client, Path(Guid.NewGuid()), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "tenant_required");
        }

        foreach (var name in new[] { null, "   ", "a\u0001b" })
        {
            using var response = await Get(client, Path(Guid.NewGuid()),
                Sign(name, ["admin"], [TenantA.ToString("D")]));
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "actor_required");
        }

        using var anonymous = await client.GetAsync(Path(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, ledger.Calls);
    }

    [TestMethod]
    public async Task EmptyIdIsNotFoundForAdminAndNeverReachesTheLedger()
    {
        var ledger = new RecordingLedgers();
        using var factory = new ApiFactory(ledger: ledger);
        using var client = factory.CreateClient();
        var admin = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        using var response = await Get(client, Path(Guid.Empty), admin);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "retrieval_not_found");
        Assert.AreEqual(0, ledger.Calls);

        // Positive control: the same admin with a non-empty ID reaches the ledger with the claim tenant.
        var id = Guid.NewGuid();
        using var control = await Get(client, Path(id), admin);
        Assert.AreEqual(HttpStatusCode.OK, control.StatusCode);
        Assert.AreEqual((TenantB, id), ledger.Last);
    }

    [TestMethod]
    public async Task AdminListsAttestationsMetadataOnlyAndOtherTenantsAndUnknownIdsAreIdentical404()
    {
        var cs = Database();
        if (cs is null) return;
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        Guid id;
        Guid emptyId;
        await using (var seed = new VeritasAtlasDbContext(Options(cs)))
        {
            var source = await new EfTenantSourceRegistry(seed, TimeProvider.System)
                .CreateAsync(TenantA, "https://example.test/r1", "synthetic-seeder", CancellationToken.None);
            var retrievals = new EfTenantRawRetrievalLedger(seed, TimeProvider.System);
            var result = await retrievals.RecordAsync(
                TenantA, new RawRetrievalInput(source!.Id, "secret-idempotency-key", Uri, At, Encoding.UTF8.GetBytes(Doc)),
                "synthetic-seeder", CancellationToken.None);
            Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, result.Outcome);
            id = result.Record!.Id;
            var other = await retrievals.RecordAsync(
                TenantA, new RawRetrievalInput(source.Id, "k2", Uri + "2", At, Encoding.UTF8.GetBytes(Doc + " ")),
                "synthetic-seeder", CancellationToken.None);
            emptyId = other.Record!.Id;

            var attestations = new EfTenantRetrievalAttestationLedger(seed, TimeProvider.System);
            foreach (var (key, publisher, minutes) in new[] { ("secret-att-key-2", "synthetic-publisher-b", 5), ("secret-att-key-1", "synthetic-publisher-a", 0) })
            {
                var recorded = await attestations.RecordAsync(
                    TenantA,
                    new RetrievalAttestationInput(
                        id, key, "synthetic-retriever", publisher, PublisherBindingMechanism.SourceRegistry,
                        "secret-binding-reference", SourceAuthenticationResult.Authenticated, "uk",
                        AttestedSourceType.Statute, At.AddMinutes(minutes)),
                    "synthetic-seeder", CancellationToken.None);
                Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, recorded.Outcome);
            }
        }

        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        using var own = await Get(client, Path(id), adminA);
        var text = await own.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode, text);
        var listed = System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateArray()
            .Select(e => e.GetProperty("publisherIdentity").GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "synthetic-publisher-a", "synthetic-publisher-b" }, listed,
            "both attestations, oldest first");
        StringAssert.Contains(text, "\"retrieverIdentity\":\"synthetic-retriever\"");
        StringAssert.Contains(text, "\"authenticationResult\":\"Authenticated\"");
        StringAssert.DoesNotMatch(text, new Regex("ev1:|Synthetic section|secret-|rawBytes|attestationKey|bindingReference"),
            "metadata only");

        // Positive control: a retrieval of the tenant that has no attestations is an empty list, not a 404.
        using var none = await Get(client, Path(emptyId), adminA);
        Assert.AreEqual(HttpStatusCode.OK, none.StatusCode);
        Assert.AreEqual("[]", await none.Content.ReadAsStringAsync());

        using var foreign = await Get(client, Path(id), adminB);
        using var unknown = await Get(client, Path(Guid.NewGuid()), adminA);
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
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
        builder.Database += "_attestreadapi";
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

    private sealed class RecordingLedgers : ITenantRawRetrievalLedger, ITenantRetrievalAttestationLedger
    {
        private int _calls;
        public int Calls => _calls;
        public (Guid Tenant, Guid Id)? Last { get; private set; }

        public Task<RawRetrievalRecordResult> RecordAsync(
            Guid tenantId, RawRetrievalInput input, string actorSubject, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantRawRetrievalModel>> ListForSourceAsync(
            Guid tenantId, Guid sourceEntryId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TenantRawRetrievalModel?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Last = (tenantId, id);
            return Task.FromResult<TenantRawRetrievalModel?>(new TenantRawRetrievalModel(
                id, Guid.NewGuid(), "k", Uri, new string('a', 64), 10, At, At));
        }

        public Task<RetrievalAttestationRecordResult> RecordAsync(
            Guid tenantId, RetrievalAttestationInput input, string actorSubject, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        Task<TenantRetrievalAttestationModel?> ITenantRetrievalAttestationLedger.GetAsync(
            Guid tenantId, Guid attestationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantRetrievalAttestationModel>> ListForRetrievalAsync(
            Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TenantRetrievalAttestationModel>>([]);
    }

    private sealed class ApiFactory(string? connectionString = null, RecordingLedgers? ledger = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0276");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                connectionString ?? "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                if (ledger is not null)
                {
                    services.AddSingleton<ITenantRawRetrievalLedger>(ledger);
                    services.AddSingleton<ITenantRetrievalAttestationLedger>(ledger);
                }
            });
        }
    }
}
