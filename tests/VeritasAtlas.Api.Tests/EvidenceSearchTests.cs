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
/// VER-P0-09 (#159): tenant-scoped search over verified evidence. Database tests run on a real
/// PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_evidencesearch</c> database).
/// All fixtures are hand-written and SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EvidenceSearchTests
{
    private const string SigningSecret = "evidence-search-test-only-key-not-for-production-01";
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

    private static EvidenceSearchRequest Req(
        string q = "section", Guid? source = null, string? jurisdiction = null, string? type = null,
        FreshnessState? freshness = null, int size = 20, string? cursor = null,
        DateTime? from = null, DateTime? to = null) =>
        new(q, source, jurisdiction, type, from, to, freshness, size, cursor);

    private static EfEvidenceSearchService Search(VeritasAtlasDbContext db, MutableTime time, SourceFreshnessPolicy? policy) =>
        new(db, new EfEvidenceFreshnessService(db, new FixedPolicies(policy), new NoProofRevisions(), time));

    [TestMethod]
    public async Task MalformedRequestsAreRefusedBeforeTheDatabaseIsTouched()
    {
        var service = new EfEvidenceSearchService(null!, null!);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(Guid.Empty, Req(), CancellationToken.None));
        foreach (var bad in new[]
                 {
                     Req(q: " "), Req(q: "a\nb"), Req(q: new string('x', EvidenceSearchLimits.MaxQueryLength + 1)),
                     Req(size: 0), Req(size: EvidenceSearchLimits.MaxPageSize + 1), Req(jurisdiction: "a\u0001"),
                     Req(type: new string('x', EvidenceSearchLimits.MaxFilterLength + 1)),
                     Req(from: At.AddDays(1), to: At), Req(freshness: (FreshnessState)99), Req(cursor: ""),
                     Req(cursor: new string('A', EvidenceSearchLimits.MaxCursorLength + 1)),
                 })
        {
            var page = await service.SearchAsync(TenantA, bad, CancellationToken.None);
            Assert.AreEqual(EvidenceSearchOutcome.InvalidRequest, page.Outcome);
            Assert.AreEqual(0, page.Hits.Count);
        }
    }

    [TestMethod]
    public async Task OnlyVerifiedEvidenceOfTheCallersTenantIsSearchable()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        await SeedAsync(db, TenantA, "r2", "beta", verify: false);
        var time = new MutableTime(At.AddMinutes(30));

        var page = await Search(db, time, Policy).SearchAsync(TenantA, Req(), CancellationToken.None);
        Assert.AreEqual(EvidenceSearchOutcome.Ok, page.Outcome);
        Assert.AreEqual(1, page.Hits.Count, "the parsed-but-unverified record must not appear");
        Assert.AreEqual(verified, page.Hits[0].EvidenceRef);
        Assert.AreEqual(Uri, page.Hits[0].SourceUri);
        Assert.AreEqual("gb-uk", page.Hits[0].Jurisdiction);
        Assert.AreEqual("Authenticated", page.Hits[0].AttestationState, ignoreCase: true);
        Assert.AreEqual(VeritasAtlas.Domain.Parsing.UkLegislationParserV1.Name, page.Hits[0].ParserName);
        Assert.AreEqual(VeritasAtlas.Domain.Parsing.UkLegislationParserV1.Version, page.Hits[0].ParserVersion);
        Assert.IsNull(page.NextCursor);

        var foreign = await Search(db, time, Policy).SearchAsync(TenantB, Req(), CancellationToken.None);
        Assert.AreEqual(0, foreign.Hits.Count, "another tenant sees nothing");
        var noMatch = await Search(db, time, Policy).SearchAsync(TenantA, Req(q: "zzzunmatched"), CancellationToken.None);
        Assert.AreEqual(0, noMatch.Hits.Count);
    }

    [TestMethod]
    public async Task FreshnessIsReEvaluatedOnEveryRequestBeforeTheCurrentFilter()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var time = new MutableTime(At.AddMinutes(30));
        var service = Search(db, time, Policy);

        var current = await service.SearchAsync(TenantA, Req(freshness: FreshnessState.Current), CancellationToken.None);
        Assert.AreEqual(1, current.Hits.Count);
        Assert.AreEqual(FreshnessState.Current, current.Hits[0].Freshness);

        time.Now = At.AddHours(3); // same indexed record, now outside the policy window
        var stillCurrent = await service.SearchAsync(TenantA, Req(freshness: FreshnessState.Current), CancellationToken.None);
        Assert.AreEqual(0, stillCurrent.Hits.Count, "a previously CURRENT record must be re-evaluated, not trusted");
        var unfiltered = await service.SearchAsync(TenantA, Req(), CancellationToken.None);
        Assert.AreEqual(FreshnessState.Stale, unfiltered.Hits.Single().Freshness);
        Assert.IsFalse(unfiltered.Hits.Single().FreshnessEligible);

        var noPolicy = await Search(db, time, null).SearchAsync(TenantA, Req(), CancellationToken.None);
        Assert.AreEqual(FreshnessState.Unknown, noPolicy.Hits.Single().Freshness, "no policy fails closed to Unknown");
    }

    [TestMethod]
    public async Task FiltersNarrowResultsByJurisdictionTypeSourceAndDate()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var source = await db.TenantRawRetrievals.AsNoTracking().Select(r => r.SourceEntryId).SingleAsync();
        var service = Search(db, new MutableTime(At.AddMinutes(30)), Policy);

        async Task<int> Count(EvidenceSearchRequest r) => (await service.SearchAsync(TenantA, r, CancellationToken.None)).Hits.Count;
        Assert.AreEqual(1, await Count(Req(jurisdiction: "GB-UK", type: "Statute", source: source, from: At.AddMinutes(-1), to: At.AddMinutes(1))));
        Assert.AreEqual(0, await Count(Req(jurisdiction: "dz")));
        Assert.AreEqual(0, await Count(Req(type: "court_record")));
        Assert.AreEqual(0, await Count(Req(source: Guid.NewGuid())));
        Assert.AreEqual(0, await Count(Req(from: At.AddHours(1))));
        Assert.AreEqual(0, await Count(Req(to: At.AddHours(-1))));
    }

    [TestMethod]
    public async Task CursorPagingIsStableAndTamperedOrForeignCursorsAreRefused()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var expected = new List<string>();
        foreach (var (key, word) in new[] { ("r1", "alpha"), ("r2", "beta"), ("r3", "gamma") })
            expected.Add(await SeedAsync(db, TenantA, key, word, verify: true));
        expected.Sort(StringComparer.Ordinal);
        var service = Search(db, new MutableTime(At.AddMinutes(30)), Policy);

        var first = await service.SearchAsync(TenantA, Req(size: 2), CancellationToken.None);
        Assert.AreEqual(2, first.Hits.Count);
        Assert.IsNotNull(first.NextCursor);
        var second = await service.SearchAsync(TenantA, Req(size: 2, cursor: first.NextCursor), CancellationToken.None);
        Assert.AreEqual(1, second.Hits.Count);
        Assert.IsNull(second.NextCursor);
        var all = first.Hits.Concat(second.Hits).Select(h => h.EvidenceRef).ToList();
        Assert.AreEqual(3, all.Distinct().Count(), "no duplicates or gaps across pages");
        CollectionAssert.AreEquivalent(expected, all);

        foreach (var bad in new[]
                 {
                     first.NextCursor + "x", "not-base64!!", Convert.ToBase64String("forged|ev1:x"u8.ToArray()),
                 })
        {
            var refused = await service.SearchAsync(TenantA, Req(size: 2, cursor: bad), CancellationToken.None);
            Assert.AreEqual(EvidenceSearchOutcome.InvalidRequest, refused.Outcome);
        }

        // Bound to the tenant and to the exact filter set that produced it.
        Assert.AreEqual(EvidenceSearchOutcome.InvalidRequest,
            (await service.SearchAsync(TenantB, Req(size: 2, cursor: first.NextCursor), CancellationToken.None)).Outcome);
        Assert.AreEqual(EvidenceSearchOutcome.InvalidRequest,
            (await service.SearchAsync(TenantA, Req(size: 2, jurisdiction: "gb-uk", cursor: first.NextCursor), CancellationToken.None)).Outcome);
    }

    [TestMethod]
    public async Task HostileQueryStringsAreHarmlessTextAndNeverBreakTheStore()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var service = Search(db, new MutableTime(At.AddMinutes(30)), Policy);

        foreach (var hostile in new[]
                 {
                     "'; DROP TABLE tenant_evidence_chunks; --", "section & | ! ( ) :* <->", "\" OR 1=1 --",
                     "%_\\", "((((", "section:*", new string('a', EvidenceSearchLimits.MaxQueryLength),
                 })
        {
            var page = await service.SearchAsync(TenantA, Req(q: hostile), CancellationToken.None);
            Assert.AreEqual(EvidenceSearchOutcome.Ok, page.Outcome, hostile);
        }

        var intact = await service.SearchAsync(TenantA, Req(), CancellationToken.None);
        Assert.AreEqual(1, intact.Hits.Count, "the store is unchanged by hostile queries");
    }

    private sealed class RecordingSearch : IEvidenceSearchService
    {
        public int Calls;
        public Guid? Tenant;
        public EvidenceSearchRequest? Last;

        public Task<EvidenceSearchPage> SearchAsync(Guid tenantId, EvidenceSearchRequest request, CancellationToken ct)
        {
            Calls++;
            Tenant = tenantId;
            Last = request;
            return Task.FromResult(new EvidenceSearchPage(EvidenceSearchOutcome.Ok, [], null));
        }
    }

    [TestMethod]
    public async Task EndpointRequiresAuthenticationAndAValidTenantAndReadsOnlyTheClaim()
    {
        var recording = new RecordingSearch();
        using var factory = new ApiFactory(recording);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        const string Path = "/api/v1/tenant-evidence/search?q=section";

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

        Assert.AreEqual(0, recording.Calls, "no search without a valid tenant");

        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);
        using (var ok = await Get(client, Path + "&freshness=current&limit=5&jurisdiction=gb-uk&tenant=" + TenantB, good))
        {
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
            StringAssert.Contains(await ok.Content.ReadAsStringAsync(), "results");
        }

        Assert.AreEqual(TenantA, recording.Tenant, "the tenant is the signed claim, never a request parameter");
        Assert.AreEqual(FreshnessState.Current, recording.Last!.Freshness);
        Assert.AreEqual(5, recording.Last.PageSize);
    }

    [TestMethod]
    public async Task EndpointRejectsInvalidParametersWithoutSearching()
    {
        var recording = new RecordingSearch();
        using var factory = new ApiFactory(recording);
        using var client = factory.CreateClient();
        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);

        foreach (var path in new[]
                 {
                     "/api/v1/tenant-evidence/search?q=a&freshness=7",
                     "/api/v1/tenant-evidence/search?q=a&freshness=Nope", "/api/v1/tenant-evidence/search?q=a&source=not-a-guid",
                 })
        {
            using var response = await Get(client, path, good);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, path);
        }

        Assert.AreEqual(0, recording.Calls, "invalid parameters must not reach the search service");
    }

    private sealed class FixedHits : IEvidenceSearchService
    {
        public Task<EvidenceSearchPage> SearchAsync(Guid tenantId, EvidenceSearchRequest request, CancellationToken ct) =>
            Task.FromResult(new EvidenceSearchPage(
                EvidenceSearchOutcome.Ok,
                [new EvidenceSearchHit(
                    "synthetic-ref-1", Uri, Guid.Parse("c3333333-3333-4333-8333-333333333333"), "gb-uk", "statute",
                    "Verified", At, FreshnessState.Superseded, false, "superseded-by-newer-revision", "2026-11-02T11:00:00Z",
                    "synthetic-parser", "9.9")],
                "next-cursor"));
    }

    /// <summary>
    /// Pins the wire contract the analyst UI (apps/web evidenceSearch.ts, #161) parses: camelCase
    /// property names, the freshness enum as its number (Current=0, Stale=1, Unknown=2, Superseded=3),
    /// and no total count.
    /// </summary>
    [TestMethod]
    public async Task ResponseWireShapeMatchesTheAnalystUiContract()
    {
        using var factory = new ApiFactory(new FixedHits());
        using var client = factory.CreateClient();
        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);

        using var response = await Get(client, "/api/v1/tenant-evidence/search?q=section", good);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        CollectionAssert.AreEquivalent(new[] { "results", "nextCursor" }, root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("next-cursor", root.GetProperty("nextCursor").GetString());
        var hit = root.GetProperty("results")[0];
        CollectionAssert.AreEquivalent(
            new[]
            {
                "evidenceRef", "sourceUri", "sourceEntryId", "jurisdiction", "sourceType", "attestationState",
                "retrievedAtUtc", "freshness", "freshnessEligible", "freshnessReason", "freshnessEvaluatedAt",
                "parserName", "parserVersion",
            },
            hit.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(System.Text.Json.JsonValueKind.Number, hit.GetProperty("freshness").ValueKind);
        Assert.AreEqual(3, hit.GetProperty("freshness").GetInt32(), "Superseded");
        Assert.IsFalse(hit.GetProperty("freshnessEligible").GetBoolean());
        Assert.AreEqual("superseded-by-newer-revision", hit.GetProperty("freshnessReason").GetString());
        Assert.AreEqual("synthetic-parser", hit.GetProperty("parserName").GetString());
        Assert.AreEqual("9.9", hit.GetProperty("parserVersion").GetString());
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

    private sealed class ApiFactory(IEvidenceSearchService search) : WebApplicationFactory<Program>
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
                services.AddSingleton(search);
            });
        }
    }

    private static async Task<string> SeedAsync(VeritasAtlasDbContext db, Guid tenant, string key, string word, bool verify)
    {
        var doc = $"<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Section {word}.</P1></Legislation>";
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
        builder.Database += "_evidencesearch";
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
