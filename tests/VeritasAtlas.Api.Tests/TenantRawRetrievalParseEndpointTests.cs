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
/// VER-P0-07 (#266): the authorized parse trigger. SYNTHETIC documents only. Role/tenant tests use a
/// recording parse service double; database-backed tests need <c>VERITAS_TEST_POSTGRES</c>
/// (own <c>*_parseapi</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantRawRetrievalParseEndpointTests
{
    private const string SigningSecret = "parse-api-test-only-key-not-for-production-0266";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private const string Doc =
        "<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Synthetic section one.</P1><P1 id=\"s2\">Synthetic section two.</P1></Legislation>";

    private static string Path(Guid id) => $"/api/v1/tenant-raw-retrievals/{id}/parses";

    [TestMethod]
    public async Task EveryNonAdminCallerIsRefusedBeforeTheParseService()
    {
        var parser = new RecordingParser();
        using var factory = new ApiFactory(parser: parser);
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
            using var response = await Post(client, Path(Guid.NewGuid()), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, label);
        }

        Assert.AreEqual(0, parser.Calls, "no parse service access for a non-admin caller");

        // Positive control: an admin of the tenant reaches the parse service.
        using var admin = await Post(client, Path(Guid.NewGuid()), Sign("synthetic-admin", ["admin"], tenant));
        Assert.AreEqual(1, parser.Calls);
        Assert.AreEqual(HttpStatusCode.Created, admin.StatusCode);
    }

    [TestMethod]
    public async Task MissingTenantActorOrAnonymousIsRefusedBeforeTheParseService()
    {
        var parser = new RecordingParser();
        using var factory = new ApiFactory(parser: parser);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-admin", "admin").Token,
                     Sign("synthetic-admin", ["admin"], ["not-a-uuid"]),
                     Sign("synthetic-admin", ["admin"], [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var response = await Post(client, Path(Guid.NewGuid()), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "tenant_required");
        }

        foreach (var name in new[] { null, "   ", "a\u0001b" })
        {
            using var response = await Post(client, Path(Guid.NewGuid()),
                Sign(name, ["admin"], [TenantA.ToString("D")]));
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "actor_required");
        }

        using var anonymous = await client.PostAsync(Path(Guid.NewGuid()), null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, parser.Calls);
    }

    [TestMethod]
    public async Task EmptyRetrievalIdIsNotFoundForAdminAndNeverReachesTheParseService()
    {
        var parser = new RecordingParser();
        using var factory = new ApiFactory(parser: parser);
        using var client = factory.CreateClient();
        using var response = await Post(client, Path(Guid.Empty), Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]));
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, text);
        StringAssert.Contains(text, "retrieval_not_found");
        Assert.AreEqual(0, parser.Calls);

        // Positive control: the same admin with a non-empty ID does reach the parse service.
        using var control = await Post(client, Path(Guid.NewGuid()), Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]));
        Assert.AreEqual(1, parser.Calls);
        Assert.AreNotEqual(HttpStatusCode.NotFound, control.StatusCode);
    }

    [TestMethod]
    public async Task TenantComesFromTheClaimAndOutcomesMapToDistinctResponses()
    {
        var run = new TenantParseRunModel(
            Guid.NewGuid(), Guid.NewGuid(), "k", "uk-legislation", "1", At,
            ParseRunOutcome.ParsedUnverified, null, At);
        var failed = run with { Outcome = ParseRunOutcome.Failed, FailureClass = ParseFailureClass.Malformed };
        var cases = new (EvidenceParseResult Result, HttpStatusCode Status, string Needle)[]
        {
            (new(EvidenceParseOutcome.ParsedUnverified, run, null, 2), HttpStatusCode.Created, "parsed_unverified"),
            (new(EvidenceParseOutcome.Replayed, run, null, 2), HttpStatusCode.OK, "replayed"),
            (new(EvidenceParseOutcome.Failed, failed, "reason with secret detail", 0), HttpStatusCode.UnprocessableEntity, "failed"),
            (new(EvidenceParseOutcome.RetrievalNotFound, null, null, 0), HttpStatusCode.NotFound, "retrieval_not_found"),
        };
        var id = Guid.NewGuid();
        foreach (var (result, status, needle) in cases)
        {
            var parser = new RecordingParser(result);
            using var factory = new ApiFactory(parser: parser);
            using var client = factory.CreateClient();
            using var response = await Post(client, Path(id), Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]));
            var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(status, response.StatusCode, text);
            StringAssert.Contains(text, needle);
            StringAssert.DoesNotMatch(text, new Regex("ev1:|secret detail"), "no evidence reference or parser reason");
            Assert.AreEqual((TenantB, id, "synthetic-admin-b"), parser.Last);
        }
    }

    [TestMethod]
    public async Task ParseStoresUnverifiedChunksReplaysAndIsTenantScoped()
    {
        var cs = Database();
        if (cs is null) return;
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        Guid good, bad;
        await using (var seed = new VeritasAtlasDbContext(Options(cs)))
        {
            good = await RetrievalAsync(seed, TenantA, "r1", Doc);
            bad = await RetrievalAsync(seed, TenantA, "r2", "<not-a-legislation-document/>");
        }

        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        // Cross-tenant and unknown ids answer the same 404 and write nothing.
        using (var foreign = await Post(client, Path(good), adminB))
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        using (var unknown = await Post(client, Path(Guid.NewGuid()), adminA))
            Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        await using (var none = new VeritasAtlasDbContext(Options(cs)))
        {
            Assert.AreEqual(0, await none.TenantParseRuns.CountAsync());
            Assert.AreEqual(0, await none.TenantEvidenceChunks.CountAsync());
        }

        using var first = await Post(client, Path(good), adminA);
        var text = await first.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, text);
        StringAssert.Contains(text, "\"chunkCount\":2");
        StringAssert.DoesNotMatch(text, new Regex("ev1:|Synthetic section"), "no reference, no chunk text");

        using var replay = await Post(client, Path(good), adminA);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        StringAssert.Contains(await replay.Content.ReadAsStringAsync(), "replayed");

        using var malformed = await Post(client, Path(bad), adminA);
        var failText = await malformed.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, malformed.StatusCode, failText);
        StringAssert.Contains(failText, "failureClass");
        StringAssert.DoesNotMatch(failText, new Regex("ev1:"));

        await using var check = new VeritasAtlasDbContext(Options(cs));
        Assert.AreEqual(2, await check.TenantParseRuns.CountAsync(), "one run per retrieval, replay adds none");
        Assert.AreEqual(2, await check.TenantEvidenceChunks.CountAsync());
        Assert.IsTrue(await check.TenantParseRuns.AllAsync(r => r.EvidenceReference == null), "nothing is verified");
        Assert.IsTrue(await check.TenantRawRetrievals.AllAsync(r => r.EvidenceReference == null));
    }

    private static async Task<Guid> RetrievalAsync(VeritasAtlasDbContext db, Guid tenant, string key, string xml)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var result = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, Uri, At, Encoding.UTF8.GetBytes(xml)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, result.Outcome);
        return result.Record!.Id;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
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
        builder.Database += "_parseapi";
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

    private sealed class RecordingParser(EvidenceParseResult? result = null) : IEvidenceParseService
    {
        private int _calls;
        public int Calls => _calls;
        public (Guid Tenant, Guid Id, string Actor)? Last { get; private set; }

        public Task<EvidenceParseResult> ParseAsync(
            Guid tenantId, Guid rawRetrievalId, string actorSubject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Last = (tenantId, rawRetrievalId, actorSubject);
            return Task.FromResult(result ?? new EvidenceParseResult(
                EvidenceParseOutcome.ParsedUnverified,
                new TenantParseRunModel(Guid.NewGuid(), rawRetrievalId, "k", "uk-legislation", "1", At,
                    ParseRunOutcome.ParsedUnverified, null, At),
                null, 1));
        }
    }

    private sealed class ApiFactory(string? connectionString = null, IEvidenceParseService? parser = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0266");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                connectionString ?? "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                if (parser is not null) services.AddSingleton(parser);
            });
        }
    }
}
