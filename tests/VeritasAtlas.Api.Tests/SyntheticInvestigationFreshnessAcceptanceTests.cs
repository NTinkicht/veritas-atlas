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
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-20 slice 1: the fake-clock stage transition of the final acceptance row, driven over the real HTTP surface
/// with real PostgreSQL (own <c>*_p020freshness</c> database). One verified synthetic evidence record is CURRENT at draft
/// time T0 and STALE before serve at T1 under a fixed fake clock. Search and the serve gate re-evaluate freshness per
/// request: the current-state claim fails closed at T1 unless new evidence is cited, the historical claim is served only
/// with its as-of date, and a missing as-of date fails the check. SYNTHETIC fixtures only.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SyntheticInvestigationFreshnessAcceptanceTests
{
    private const string SigningSecret = "p020-freshness-acceptance-test-only-key-not-for-production-01";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private static readonly SourceFreshnessPolicy Policy =
        new("p1", "v1", "synthetic", true, 3600, "etag", false, "registry");
    private static readonly string ParserName = VeritasAtlas.Domain.Parsing.UkLegislationParserV1.Name;
    private static readonly string ParserVersion = VeritasAtlas.Domain.Parsing.UkLegislationParserV1.Version;

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

    private static object Claim(string id, string evidenceRef, bool current, string? asOf = null) => new
    {
        claimId = id, currentState = current, historicalAsOf = asOf,
        citations = new[] { new { evidenceRef, parserName = ParserName, parserVersion = ParserVersion } },
    };

    private static async Task<JsonElement> ServeCheck(HttpClient client, string bearer, object claim)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenant-answers/serve-check");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Content = JsonContent.Create(new { claims = new[] { claim }, humanVerification = "Verified" });
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string[] Codes(JsonElement r) =>
        r.GetProperty("blockers").EnumerateArray().Select(b => b.GetProperty("code").GetString()!).ToArray();

    private static async Task<JsonElement> Search(HttpClient client, string bearer, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenant-evidence/search?q=section" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [TestMethod]
    public async Task EvidenceCurrentAtDraftTimeIsStaleBeforeServeAndTheGateFailsClosedWithoutNewEvidence()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        string evidenceRef;
        await using (var db = new VeritasAtlasDbContext(Options(cs)))
        {
            evidenceRef = await SeedAsync(db, TenantA, "r1", "alpha");
        }

        var time = new MutableTime(At.AddMinutes(30)); // T0: inside the 3600s policy window
        using var factory = new ApiFactory(cs, time);
        using var client = factory.CreateClient();
        var analyst = Sign("analyst", "analyst", TenantA);

        // T0: the draft stage sees CURRENT evidence and the gate serves the current-state claim.
        var hitsT0 = await Search(client, analyst, "&freshness=current");
        Assert.AreEqual(1, hitsT0.GetProperty("results").GetArrayLength());
        var servedT0 = await ServeCheck(client, analyst, Claim("c1", evidenceRef, current: true));
        Assert.AreEqual("serve", servedT0.GetProperty("decision").GetString());
        Assert.AreEqual(0, Codes(servedT0).Length);

        // T1: same stored evidence, clock outside the policy window.
        time.Now = At.AddHours(3);
        var hitsT1 = await Search(client, analyst, "&freshness=current");
        Assert.AreEqual(0, hitsT1.GetProperty("results").GetArrayLength(), "an expired record leaves CURRENT-filtered results");
        var unfiltered = await Search(client, analyst, string.Empty);
        Assert.AreEqual((int)FreshnessState.Stale, unfiltered.GetProperty("results")[0].GetProperty("freshness").GetInt32());

        var blocked = await ServeCheck(client, analyst, Claim("c1", evidenceRef, current: true));
        Assert.AreEqual("block", blocked.GetProperty("decision").GetString());
        CollectionAssert.AreEqual(new[] { ServeBlockers.CurrentStateNotCurrent }, Codes(blocked));

        // A historical claim with its as-of date is policy-permitted and served; without the date it fails the check.
        var historical = await ServeCheck(client, analyst, Claim("c2", evidenceRef, current: false, asOf: "2020-01-01"));
        Assert.AreEqual("serve", historical.GetProperty("decision").GetString());
        var undated = await ServeCheck(client, analyst, Claim("c2", evidenceRef, current: false));
        Assert.AreEqual("block", undated.GetProperty("decision").GetString());
        Assert.IsTrue(Codes(undated).Contains(ServeBlockers.HistoricalAsOfMissing), string.Join(",", Codes(undated)));

        // New verified evidence cited by a new current claim is served at T1 (the block is caused by staleness, not the gate).
        string fresh;
        await using (var db = new VeritasAtlasDbContext(Options(cs)))
        {
            fresh = await SeedAsync(db, TenantA, "r2", "beta", retrievedAt: At.AddHours(3).AddMinutes(-10));
        }

        var renewed = await ServeCheck(client, analyst, Claim("c3", fresh, current: true));
        Assert.AreEqual("serve", renewed.GetProperty("decision").GetString());

        // Tenant isolation: another tenant cannot rely on tenant A's evidence at either time.
        var other = await ServeCheck(client, Sign("analyst-b", "analyst", TenantB), Claim("c1", fresh, current: true));
        Assert.AreEqual("block", other.GetProperty("decision").GetString());
        Assert.IsTrue(Codes(other).Contains(ServeBlockers.CitationNotVerified));
    }

    private static async Task<string> SeedAsync(VeritasAtlasDbContext db, Guid tenant, string key, string word, DateTime? retrievedAt = null)
    {
        var doc = $"<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Section {word}.</P1></Legislation>";
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var recorded = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, Uri, retrievedAt ?? At, Encoding.UTF8.GetBytes(doc)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, recorded.Outcome);
        var retrieval = recorded.Record!.Id;
        var run = (await new EfEvidenceParseService(db, TimeProvider.System)
            .ParseAsync(tenant, retrieval, "synthetic-seeder", CancellationToken.None)).Run!.Id;
        var attested = await new EfTenantRetrievalAttestationLedger(db, TimeProvider.System).RecordAsync(
            tenant,
            new RetrievalAttestationInput(
                retrieval, "k-" + key, "synthetic-retriever", "synthetic-publisher", PublisherBindingMechanism.SourceRegistry,
                "registry-entry-1", SourceAuthenticationResult.Authenticated, "gb-uk", AttestedSourceType.Statute, (retrievedAt ?? At).AddMinutes(5)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, attested.Outcome);
        var verified = await new EfEvidenceVerificationService(db, new EchoVerifier(), new MutableTime((retrievedAt ?? At).AddMinutes(10)))
            .VerifyAsync(tenant, run, new EvidenceClassificationExpectation("gb-uk", "statute"), "synthetic-verifier", CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.Verified, verified.Outcome);
        return verified.Verification!.ParentEvidenceRef;
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
        builder.Database += "_p020freshness";
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

    private static string Sign(string name, string role, Guid tenant)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, name), new(ClaimTypes.Role, role), new(TenantClaim.Type, tenant.ToString("D")),
        };
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: RuntimeSecurity.DefaultJwtIssuer, audience: RuntimeSecurity.DefaultJwtAudience, claims: claims,
            notBefore: now.AddMinutes(-1), expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)), SecurityAlgorithms.HmacSha256)));
    }

    private sealed class ApiFactory(string connectionString, TimeProvider time) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0020");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                services.AddSingleton(time);
                services.AddSingleton<ISourceFreshnessPolicyResolver>(new FixedPolicies(Policy));
                services.AddSingleton<ISourceRevisionEvidenceProvider>(new NoProofRevisions());
            });
        }
    }
}
