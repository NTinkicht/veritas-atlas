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
using VeritasAtlas.Domain.Parsing;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-11 slice 2: tenant-scoped read-only claim candidates over verified evidence. Database tests run on a real
/// PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_claimcandidates</c> database).
/// All fixtures are hand-written and SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ClaimCandidateTests
{
    private const string SigningSecret = "claim-candidates-test-only-key-not-for-production-01";
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

    private static EfClaimCandidateService Service(VeritasAtlasDbContext db, MutableTime time, SourceFreshnessPolicy? policy) =>
        new(db, new EfEvidenceFreshnessService(db, new FixedPolicies(policy), new NoProofRevisions(), time), new ClaimExtractionV1());

    [TestMethod]
    public async Task MalformedReferencesAreRefusedBeforeTheDatabaseIsTouched()
    {
        var service = new EfClaimCandidateService(null!, null!, new ClaimExtractionV1());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ListAsync(Guid.Empty, "ev1:x", CancellationToken.None));
        foreach (var bad in new[] { "", " ", "a\nb", new string('x', ClaimCandidateLimits.MaxEvidenceRefLength + 1) })
            Assert.AreEqual(ClaimCandidateOutcome.InvalidRequest, (await service.ListAsync(TenantA, bad, CancellationToken.None)).Outcome);
    }

    [TestMethod]
    public async Task VerifiedEvidenceYieldsUnconfirmedExactSpanCandidatesAndNothingIsWritten()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var chunk = await db.TenantEvidenceChunks.AsNoTracking().SingleAsync();
        var before = (await db.TenantEvidenceChunks.CountAsync(), await db.TenantEvidenceVerifications.CountAsync());

        var result = await Service(db, new MutableTime(At.AddMinutes(30)), Policy).ListAsync(TenantA, verified, CancellationToken.None);
        Assert.AreEqual(ClaimCandidateOutcome.Ok, result.Outcome);
        Assert.AreEqual(2, result.Claims.Count, "only the two normative sentences are candidates");
        foreach (var claim in result.Claims)
        {
            Assert.AreEqual(ClaimStatus.Unconfirmed, claim.Status);
            Assert.AreEqual(TenantA, claim.TenantId);
            Assert.AreEqual(verified, claim.Span.ParentEvidenceRef);
            Assert.AreEqual(claim.Quotation, chunk.Text.Substring(claim.Span.Start, claim.Span.Length), "exact stored text");
            Assert.IsFalse(ClaimExtractionV1.IsAnswerable(claim, TenantA, new EvidenceChunk(
                chunk.ParentEvidenceRef, chunk.Ordinal, chunk.ElementId, chunk.Text, chunk.TextSha256)));
        }

        Assert.AreEqual(FreshnessState.Current, result.Freshness!.State);
        Assert.AreEqual(before, (await db.TenantEvidenceChunks.CountAsync(), await db.TenantEvidenceVerifications.CountAsync()));
    }

    [TestMethod]
    public async Task UnverifiedUnknownAndForeignReferencesAreOneUniformNotFound()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var unverified = await SeedAsync(db, TenantA, "r2", "beta", verify: false);
        var service = Service(db, new MutableTime(At.AddMinutes(30)), Policy);

        foreach (var (tenant, reference) in new[] { (TenantA, unverified), (TenantA, "ev1:unknown"), (TenantB, verified) })
        {
            var result = await service.ListAsync(tenant, reference, CancellationToken.None);
            Assert.AreEqual(ClaimCandidateOutcome.NotFound, result.Outcome, reference);
            Assert.AreEqual(0, result.Claims.Count);
            Assert.IsNull(result.Freshness);
        }
    }

    [TestMethod]
    public async Task FreshnessIsReEvaluatedOnEveryCallAndFailsClosedWithoutPolicy()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var time = new MutableTime(At.AddMinutes(30));
        var service = Service(db, time, Policy);

        var fresh = await service.ListAsync(TenantA, verified, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Current, fresh.Freshness!.State);
        time.Now = At.AddHours(3);
        var aged = await service.ListAsync(TenantA, verified, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Stale, aged.Freshness!.State, "an earlier CURRENT result is never reused");
        Assert.IsFalse(aged.Freshness.Eligible);
        Assert.AreEqual(2, aged.Claims.Count, "stale evidence is labelled, not hidden");
        var noPolicy = await Service(db, time, null).ListAsync(TenantA, verified, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Unknown, noPolicy.Freshness!.State);
    }

    private sealed class FailingExtractor : IClaimExtractor
    {
        public ClaimExtractionResult Extract(Guid tenantId, IReadOnlyList<EvidenceChunk> chunks) =>
            ClaimExtractionResult.Fail(ClaimExtractionFailure.InvalidChunk, "chunk_digest_mismatch");
    }

    [TestMethod]
    public async Task FailedExtractionIntegrityRefusesTheWholeResult()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var service = new EfClaimCandidateService(
            db, new EfEvidenceFreshnessService(db, new FixedPolicies(Policy), new NoProofRevisions(), new MutableTime(At.AddMinutes(30))),
            new FailingExtractor());

        var result = await service.ListAsync(TenantA, verified, CancellationToken.None);
        Assert.AreEqual(ClaimCandidateOutcome.ExtractionRefused, result.Outcome);
        Assert.AreEqual(0, result.Claims.Count);
        Assert.IsNull(result.Freshness);
    }

    private sealed class FixedResult(ClaimCandidateResult result) : IClaimCandidateService
    {
        public int Calls;
        public Guid? Tenant;
        public string? Ref;

        public Task<ClaimCandidateResult> ListAsync(Guid tenantId, string evidenceRef, CancellationToken ct)
        {
            Calls++;
            Tenant = tenantId;
            Ref = evidenceRef;
            return Task.FromResult(result);
        }
    }

    [TestMethod]
    public async Task EndpointRequiresAuthenticationAndAValidTenantAndReadsOnlyTheClaim()
    {
        var fixedResult = new FixedResult(new(ClaimCandidateOutcome.NotFound, [], null));
        using var factory = new ApiFactory(fixedResult);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        const string Path = "/api/v1/tenant-evidence/claim-candidates?evidenceRef=ev1:abc";

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

        Assert.AreEqual(0, fixedResult.Calls);
        using (var notFound = await Get(client, Path + "&tenant=" + TenantB, Sign("synthetic-an", "analyst", [TenantA.ToString("D")])))
            Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.AreEqual(TenantA, fixedResult.Tenant, "the tenant is the signed claim, never a request parameter");
        Assert.AreEqual("ev1:abc", fixedResult.Ref);
    }

    [TestMethod]
    public async Task EndpointMapsOutcomesAndPinsTheWireShape()
    {
        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);
        var claim = new ExtractedClaim(
            TenantA, "key1", "It shall apply.", new ClaimEvidenceSpan("ev1:abc", "s1", "deadbeef", 3, 15), ClaimStatus.Unconfirmed);
        var ok = new FixedResult(new(ClaimCandidateOutcome.Ok, [claim],
            new ClaimCandidateFreshness(FreshnessState.Stale, false, "age-window-exceeded", "2026-11-02T11:00:00Z")));
        using (var factory = new ApiFactory(ok))
        using (var client = factory.CreateClient())
        {
            using var response = await Get(client, "/api/v1/tenant-evidence/claim-candidates?evidenceRef=ev1:abc", good);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;
            CollectionAssert.AreEquivalent(
                new[] { "evidenceRef", "extractorName", "extractorVersion", "freshness", "claims" },
                root.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.AreEqual("stale", root.GetProperty("freshness").GetProperty("state").GetString());
            Assert.IsFalse(root.GetProperty("freshness").GetProperty("eligible").GetBoolean());
            var item = root.GetProperty("claims")[0];
            CollectionAssert.AreEquivalent(
                new[] { "claimKey", "quotation", "elementId", "start", "length", "chunkTextSha256", "status", "answerable" },
                item.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.AreEqual("unconfirmed", item.GetProperty("status").GetString());
            Assert.IsFalse(item.GetProperty("answerable").GetBoolean());
        }

        foreach (var (outcome, path, expected, code) in new[]
                 {
                     (ClaimCandidateOutcome.InvalidRequest, "/api/v1/tenant-evidence/claim-candidates?evidenceRef=bad", HttpStatusCode.BadRequest, "invalid_evidence_ref"),
                     (ClaimCandidateOutcome.InvalidRequest, "/api/v1/tenant-evidence/claim-candidates", HttpStatusCode.BadRequest, "invalid_evidence_ref"),
                     (ClaimCandidateOutcome.ExtractionRefused, "/api/v1/tenant-evidence/claim-candidates?evidenceRef=ev1:abc", HttpStatusCode.Conflict, "extraction_refused"),
                 })
        {
            using var factory = new ApiFactory(new FixedResult(new(outcome, [], null)));
            using var client = factory.CreateClient();
            using var response = await Get(client, path, good);
            Assert.AreEqual(expected, response.StatusCode, path);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), code);
        }
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

    private sealed class ApiFactory(IClaimCandidateService candidates) : WebApplicationFactory<Program>
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
                services.AddSingleton(candidates);
            });
        }
    }

    private static async Task<string> SeedAsync(VeritasAtlasDbContext db, Guid tenant, string key, string word, bool verify)
    {
        var doc = $"<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Section {word} shall apply. It is informative only. The holder may not transfer it.</P1></Legislation>";
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
        builder.Database += "_claimcandidates";
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
