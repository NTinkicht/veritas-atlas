using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
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
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-17 (#342): tenant-scoped detail read of one verified evidence item. Database tests run on a real
/// PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_evidencedetail</c> database).
/// All fixtures are hand-written and SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EvidenceDetailTests
{
    private const string SigningSecret = "evidence-detail-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private static readonly SourceFreshnessPolicy Policy =
        new("p1", "v1", "synthetic", true, 3600, "etag", false, "registry");

    private sealed class FixedPolicies(SourceFreshnessPolicy? policy) : ISourceFreshnessPolicyResolver
    {
        public Task<SourceFreshnessPolicyResolution?> ResolveAsync(Guid t, Guid s, CancellationToken ct) =>
            Task.FromResult<SourceFreshnessPolicyResolution?>(policy is null ? null : new(policy, policy));
    }

    private sealed class NoProofRevisions : ISourceRevisionEvidenceProvider
    {
        public Task<SourceRevisionEvidence> GetAsync(Guid t, Guid r, CancellationToken ct) =>
            Task.FromResult(new SourceRevisionEvidence(false, false, false));
    }

    private sealed class EchoVerifier : IPublisherBindingVerifier
    {
        public Task<PublisherBindingVerification> VerifyAsync(AttestationClaim c, CancellationToken ct) =>
            Task.FromResult(new PublisherBindingVerification(
                c.PublisherIdentity, c.BindingMechanism, c.BindingReference, c.FinalUri, c.ContentSha256, true));
    }

    private sealed class MutableTime(DateTime utc) : TimeProvider
    {
        public DateTime Now { get; set; } = utc;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private static EfEvidenceDetailService Detail(VeritasAtlasDbContext db, MutableTime time, SourceFreshnessPolicy? policy) =>
        new(db, new EfEvidenceFreshnessService(db, new FixedPolicies(policy), new NoProofRevisions(), time));

    [TestMethod]
    public async Task MalformedReferencesAreRefusedBeforeTheDatabaseIsTouched()
    {
        var service = new EfEvidenceDetailService(null!, null!);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.GetAsync(Guid.Empty, "ev1:x", CancellationToken.None));
        foreach (var bad in new[] { "", " ", "a\nb", "a\u0001", new string('x', EvidenceDetailLimits.MaxRefLength + 1) })
        {
            var result = await service.GetAsync(TenantA, bad, CancellationToken.None);
            Assert.AreEqual(EvidenceDetailOutcome.InvalidRequest, result.Outcome);
            Assert.IsNull(result.Detail);
        }
    }

    [TestMethod]
    public async Task OnlyVerifiedEvidenceOfTheCallersTenantHasADetail()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var unverified = await SeedAsync(db, TenantA, "r2", "beta", verify: false);
        var service = Detail(db, new MutableTime(At.AddMinutes(30)), Policy);

        var ok = await service.GetAsync(TenantA, verified, CancellationToken.None);
        Assert.AreEqual(EvidenceDetailOutcome.Ok, ok.Outcome);
        var d = ok.Detail!;
        Assert.AreEqual(verified, d.EvidenceRef);
        Assert.AreEqual(Uri, d.SourceUri);
        Assert.AreEqual("gb-uk", d.Jurisdiction);
        Assert.AreEqual("Authenticated", d.AttestationState, ignoreCase: true);
        Assert.AreEqual(VeritasAtlas.Domain.Parsing.UkLegislationParserV1.Name, d.ParserName);
        Assert.AreEqual(VeritasAtlas.Domain.Parsing.UkLegislationParserV1.Version, d.ParserVersion);
        Assert.AreEqual(FreshnessState.Current, d.Freshness);
        Assert.IsTrue(d.FreshnessEligible);
        Assert.IsFalse(d.ChunksTruncated);
        Assert.AreEqual(1, d.Chunks.Count);
        Assert.AreEqual("Section alpha.", d.Chunks[0].Text);

        foreach (var (tenant, reference) in new[] { (TenantA, unverified), (TenantB, verified), (TenantA, "ev1:unknown") })
        {
            var refused = await service.GetAsync(tenant, reference, CancellationToken.None);
            Assert.AreEqual(EvidenceDetailOutcome.NotFound, refused.Outcome, reference);
            Assert.IsNull(refused.Detail);
        }
    }

    [TestMethod]
    public async Task FreshnessIsReEvaluatedOnEveryReadAndFailsClosed()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var reference = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var time = new MutableTime(At.AddMinutes(30));
        var service = Detail(db, time, Policy);

        Assert.AreEqual(FreshnessState.Current, (await service.GetAsync(TenantA, reference, CancellationToken.None)).Detail!.Freshness);
        time.Now = At.AddHours(3);
        var stale = (await service.GetAsync(TenantA, reference, CancellationToken.None)).Detail!;
        Assert.AreEqual(FreshnessState.Stale, stale.Freshness, "a previously CURRENT record must be re-evaluated, not trusted");
        Assert.IsFalse(stale.FreshnessEligible);
        var noPolicy = (await Detail(db, time, null).GetAsync(TenantA, reference, CancellationToken.None)).Detail!;
        Assert.AreEqual(FreshnessState.Unknown, noPolicy.Freshness, "no policy fails closed to Unknown");
    }

    [TestMethod]
    public async Task ChunkTextIsBoundedAndFlaggedTruncated()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var filler = " " + string.Concat(Enumerable.Repeat("word ", 1200));
        var reference = await SeedAsync(db, TenantA, "r1", "alpha", verify: true, filler: filler);
        var detail = (await Detail(db, new MutableTime(At.AddMinutes(30)), Policy)
            .GetAsync(TenantA, reference, CancellationToken.None)).Detail!;

        Assert.IsTrue(detail.ChunksTruncated);
        Assert.AreEqual(EvidenceDetailLimits.MaxChunkLength, detail.Chunks.Single().Text.Length);
        Assert.IsTrue(detail.Chunks[0].Text.StartsWith("Section alpha.", StringComparison.Ordinal));
    }

    private sealed class RecordingDetail : IEvidenceDetailService
    {
        public int Calls;
        public Guid? Tenant;
        public string? Ref;
        public EvidenceDetailOutcome Outcome = EvidenceDetailOutcome.Ok;

        public Task<EvidenceDetailResult> GetAsync(Guid tenantId, string evidenceRef, CancellationToken ct)
        {
            Calls++;
            Tenant = tenantId;
            Ref = evidenceRef;
            return Task.FromResult(new EvidenceDetailResult(
                Outcome,
                Outcome != EvidenceDetailOutcome.Ok ? null : new EvidenceDetail(
                    evidenceRef, Uri, Guid.Parse("c3333333-3333-4333-8333-333333333333"), "gb-uk", "statute",
                    "Verified", At, FreshnessState.Superseded, false, "superseded-by-newer-revision", "2026-11-02T11:00:00Z",
                    "synthetic-parser", "9.9", [new EvidenceDetailChunk(0, "s1", "Section alpha.")], false)));
        }
    }

    [TestMethod]
    public async Task EndpointRequiresAuthenticationAndAValidTenantAndReadsOnlyTheClaim()
    {
        var recording = new RecordingDetail();
        using var factory = new ApiFactory(recording);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        const string Path = "/api/v1/tenant-evidence/detail?ref=ev1%3Asynthetic";

        using (var anonymous = await client.GetAsync(Path))
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-op", "operator").Token,
                     Sign("synthetic-x", "analyst", ["not-a-uuid"]),
                     Sign("synthetic-x", "analyst", [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var response = await Get(client, Path, bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "tenant_required");
        }

        Assert.AreEqual(0, recording.Calls, "no read without a valid tenant");

        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);
        using (var ok = await Get(client, Path + "&tenant=" + TenantB, good))
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
        Assert.AreEqual(TenantA, recording.Tenant, "the tenant is the signed claim, never a request parameter");
        Assert.AreEqual("ev1:synthetic", recording.Ref);
    }

    [TestMethod]
    public async Task EndpointMapsNotFoundAndInvalidAndPinsTheWireShape()
    {
        var recording = new RecordingDetail();
        using var factory = new ApiFactory(recording);
        using var client = factory.CreateClient();
        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);

        recording.Outcome = EvidenceDetailOutcome.NotFound;
        using (var missing = await Get(client, "/api/v1/tenant-evidence/detail?ref=ev1%3Ax", good))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
            StringAssert.Contains(await missing.Content.ReadAsStringAsync(), "evidence_not_found");
        }

        recording.Outcome = EvidenceDetailOutcome.InvalidRequest;
        using (var invalid = await Get(client, "/api/v1/tenant-evidence/detail", good))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            StringAssert.Contains(await invalid.Content.ReadAsStringAsync(), "invalid_evidence_ref");
        }

        recording.Outcome = EvidenceDetailOutcome.Ok;
        using var response = await Get(client, "/api/v1/tenant-evidence/detail?ref=ev1%3Ax", good);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(
            new[]
            {
                "evidenceRef", "sourceUri", "sourceEntryId", "jurisdiction", "sourceType", "attestationState",
                "retrievedAtUtc", "freshness", "freshnessEligible", "freshnessReason", "freshnessEvaluatedAt",
                "parserName", "parserVersion", "chunks", "chunksTruncated",
            },
            body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(3, body.RootElement.GetProperty("freshness").GetInt32(), "Superseded");
        CollectionAssert.AreEquivalent(
            new[] { "ordinal", "elementId", "text" },
            body.RootElement.GetProperty("chunks")[0].EnumerateObject().Select(p => p.Name).ToArray());
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private static string Sign(string name, string role, string[] tenants)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role),
        };
        claims.AddRange(tenants.Select(t => new Claim(TenantClaim.Type, t)));
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: RuntimeSecurity.DefaultJwtIssuer,
            audience: RuntimeSecurity.DefaultJwtAudience,
            claims: claims,
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)), SecurityAlgorithms.HmacSha256)));
    }

    private sealed class ApiFactory(IEvidenceDetailService detail) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0009");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                services.AddSingleton(detail);
            });
        }
    }

    private static async Task<string> SeedAsync(VeritasAtlasDbContext db, Guid tenant, string key, string word, bool verify, string filler = "")
    {
        var doc = $"<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Section {word}.{filler}</P1></Legislation>";
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var recorded = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, Uri, At, Encoding.UTF8.GetBytes(doc)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, recorded.Outcome);
        var retrieval = recorded.Record!.Id;
        var run = (await new EfEvidenceParseService(db, TimeProvider.System)
            .ParseAsync(tenant, retrieval, "synthetic-seeder", CancellationToken.None)).Run!.Id;
        if (!verify)
        {
            return await db.TenantEvidenceChunks.AsNoTracking()
                .Where(c => c.TenantId == tenant && c.ParseRunId == run)
                .Select(c => c.ParentEvidenceRef).FirstAsync();
        }

        var attested = await new EfTenantRetrievalAttestationLedger(db, TimeProvider.System).RecordAsync(
            tenant,
            new RetrievalAttestationInput(
                retrieval, "k-" + key, "synthetic-retriever", "synthetic-publisher", PublisherBindingMechanism.SourceRegistry,
                "registry-entry-1", SourceAuthenticationResult.Authenticated, "gb-uk", AttestedSourceType.Statute, At.AddMinutes(5)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, attested.Outcome);
        var verified = await new EfEvidenceVerificationService(db, new EchoVerifier(), new FixedTimeAt(At.AddHours(2)))
            .VerifyAsync(tenant, run, new EvidenceClassificationExpectation("gb-uk", "statute"), "synthetic-verifier", CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.Verified, verified.Outcome);
        return verified.Verification!.ParentEvidenceRef;
    }

    private sealed class FixedTimeAt(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
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
        builder.Database += "_evidencedetail";
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
}
