using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
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
/// VER-P0-12 (#182) slice 1: claim-to-evidence validation against the citation-integrity/v1 matrix.
/// Database tests run on a real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own
/// <c>*_claimevidence</c> database). Each negative case mutates exactly one component. SYNTHETIC fixtures.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ClaimEvidenceValidationTests
{
    private const string SigningSecret = "claim-evidence-test-only-key-not-for-production-01";
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

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private static EfClaimEvidenceValidationService Validator(
        VeritasAtlasDbContext db, DateTime now, SourceFreshnessPolicy? policy) =>
        new(db, new EfEvidenceFreshnessService(db, new FixedPolicies(policy), new NoProofRevisions(), new FixedTime(now)));

    private static ClaimEvidenceCitation Cite(string evidenceRef, string? name = null, string? version = null, Guid? scope = null) =>
        new(evidenceRef, name ?? ParserName, version ?? ParserVersion, scope);

    private static ClaimEvidenceValidationRequest Claim(
        bool required, SourceFreshnessClaimPolicy? policy, params ClaimEvidenceCitation[] citations) =>
        new(required, policy ?? SourceFreshnessClaimPolicy.CurrentOnly, citations);

    private static readonly DateTime Fresh = At.AddMinutes(30);
    private static readonly DateTime Aged = At.AddHours(3);

    [TestMethod]
    public async Task EvidenceRequiredWithNoCitationIsRejectedAndMalformedRequestsAreRefusedBeforeTheDatabase()
    {
        var service = new EfClaimEvidenceValidationService(null!, null!);
        var required = await service.ValidateAsync(TenantA, Claim(true, null), CancellationToken.None);
        Assert.IsFalse(required.Valid);
        Assert.AreEqual(ClaimEvidenceCodes.Required, required.Code);
        var optional = await service.ValidateAsync(TenantA, Claim(false, null), CancellationToken.None);
        Assert.IsTrue(optional.Valid);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ValidateAsync(Guid.Empty, Claim(true, null), CancellationToken.None));
        var tooMany = Enumerable.Range(0, ClaimEvidenceLimits.MaxCitations + 1).Select(_ => Cite("ev1:" + new string('a', 64))).ToArray();
        foreach (var bad in new[]
                 {
                     new ClaimEvidenceValidationRequest(true, null, null),
                     Claim(true, null, tooMany),
                     Claim(true, null, Cite("ev1:" + new string('a', 64), name: " ")),
                     Claim(true, null, Cite("ev1:" + new string('a', 64), version: "1\n2")),
                     Claim(true, null, Cite("ev1:" + new string('a', 64), name: new string('x', ClaimEvidenceLimits.MaxTextLength + 1))),
                     new ClaimEvidenceValidationRequest(true, null, [null!]),
                 })
        {
            var result = await service.ValidateAsync(TenantA, bad, CancellationToken.None);
            Assert.AreEqual(ClaimEvidenceValidationOutcome.InvalidRequest, result.Outcome);
            Assert.IsFalse(result.Valid);
        }
    }

    [TestMethod]
    public async Task ExactVerifiedEvidenceIsValidAndReportsItsCurrentFreshness()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);

        var result = await Validator(db, Fresh, Policy).ValidateAsync(TenantA, Claim(true, null, Cite(r1)), CancellationToken.None);
        Assert.IsTrue(result.Valid, result.Code);
        Assert.IsNull(result.Code);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(FreshnessState.Current, result.Items[0].Freshness);
        Assert.IsNull(result.Items[0].HistoricalAsOf);
    }

    [TestMethod]
    public async Task UnknownMalformedUnverifiedAndOtherTenantReferencesAreIndistinguishableNotFound()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var unverified = await SeedAsync(db, TenantA, "r2", "beta", verify: false);
        var validator = Validator(db, Fresh, Policy);

        var missing = "ev1:" + new string('0', 64);
        foreach (var candidate in new[] { missing, "not-a-reference", unverified, string.Empty })
        {
            var result = await validator.ValidateAsync(TenantA, Claim(true, null, Cite(candidate)), CancellationToken.None);
            Assert.IsFalse(result.Valid);
            Assert.AreEqual(ClaimEvidenceCodes.NotFound, result.Code, candidate);
        }

        var foreign = await validator.ValidateAsync(TenantB, Claim(true, null, Cite(verified)), CancellationToken.None);
        var unknown = await validator.ValidateAsync(TenantB, Claim(true, null, Cite(missing)), CancellationToken.None);
        Assert.AreEqual(ClaimEvidenceCodes.NotFound, foreign.Code, "another tenant's reference must not be revealed");
        Assert.AreEqual(unknown.Code, foreign.Code);
        Assert.AreEqual(unknown.Items[0].Freshness, foreign.Items[0].Freshness);
    }

    [TestMethod]
    public async Task ParserDriftAgainstTheRecordedParserIsRejectedWhateverTheCurrentlyDeployedOneIs()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var validator = Validator(db, Fresh, Policy);

        foreach (var drifted in new[] { Cite(r1, version: ParserVersion + ".1"), Cite(r1, name: ParserName + "-v2"), Cite(r1, version: "v2") })
        {
            var result = await validator.ValidateAsync(TenantA, Claim(true, null, drifted), CancellationToken.None);
            Assert.AreEqual(ClaimEvidenceCodes.ParserMismatch, result.Code);
        }
    }

    [TestMethod]
    public async Task EveryCitedItemMustValidateAndOneInvalidItemRejectsTheClaim()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var r2 = await SeedAsync(db, TenantA, "r2", "beta", verify: true);
        var validator = Validator(db, Fresh, Policy);

        var both = await validator.ValidateAsync(TenantA, Claim(true, null, Cite(r1), Cite(r2)), CancellationToken.None);
        Assert.IsTrue(both.Valid);
        var oneBad = await validator.ValidateAsync(
            TenantA, Claim(true, null, Cite(r1), Cite(r2, version: "other")), CancellationToken.None);
        Assert.IsFalse(oneBad.Valid);
        Assert.AreEqual(ClaimEvidenceCodes.ParserMismatch, oneBad.Code);
        Assert.IsTrue(oneBad.Items[0].Valid);
        Assert.IsFalse(oneBad.Items[1].Valid);
    }

    [TestMethod]
    public async Task AScopePinnedToAnotherSourceIsRejectedAndTheOwnSourceIsAccepted()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var source = await db.TenantRawRetrievals.AsNoTracking().Where(r => r.TenantId == TenantA).Select(r => r.SourceEntryId).SingleAsync();
        var validator = Validator(db, Fresh, Policy);

        var own = await validator.ValidateAsync(TenantA, Claim(true, null, Cite(r1, scope: source)), CancellationToken.None);
        Assert.IsTrue(own.Valid);
        var other = await validator.ValidateAsync(TenantA, Claim(true, null, Cite(r1, scope: Guid.NewGuid())), CancellationToken.None);
        Assert.AreEqual(ClaimEvidenceCodes.ScopeMismatch, other.Code);
    }

    [TestMethod]
    public async Task AnAttestationThatIsNoLongerAuthenticatedMakesProvenanceIncomplete()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE tenant_retrieval_attestations DISABLE TRIGGER tr_tenant_retrieval_attestations_append_only");
        await db.Database.ExecuteSqlRawAsync("UPDATE tenant_retrieval_attestations SET authentication_result = 'failed'");

        var result = await Validator(db, Fresh, Policy).ValidateAsync(TenantA, Claim(true, null, Cite(r1)), CancellationToken.None);
        Assert.AreEqual(ClaimEvidenceCodes.ProvenanceIncomplete, result.Code);
    }

    [TestMethod]
    public async Task StoredBytesThatNoLongerMatchTheDigestAreRejected()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE tenant_raw_retrievals DISABLE TRIGGER tr_tenant_raw_retrievals_immutable");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE tenant_raw_retrievals DROP CONSTRAINT ck_tenant_raw_retrievals_content_sha256_matches_bytes");
        await db.Database.ExecuteSqlRawAsync("UPDATE tenant_raw_retrievals SET raw_bytes = convert_to(repeat('A', byte_length::int), 'UTF8')");

        var result = await Validator(db, Fresh, Policy).ValidateAsync(TenantA, Claim(true, null, Cite(r1)), CancellationToken.None);
        Assert.AreEqual(ClaimEvidenceCodes.DigestMismatch, result.Code);
    }

    [TestMethod]
    public async Task FreshnessIsEvaluatedAtValidationTimeAndStaleOrUnknownEvidenceIsRejected()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var claim = Claim(true, null, Cite(r1));

        Assert.IsTrue((await Validator(db, Fresh, Policy).ValidateAsync(TenantA, claim, CancellationToken.None)).Valid);
        var stale = await Validator(db, Aged, Policy).ValidateAsync(TenantA, claim, CancellationToken.None);
        Assert.AreEqual(ClaimEvidenceCodes.Stale, stale.Code, "the same evidence expires with the server clock");
        Assert.AreEqual(FreshnessState.Stale, stale.Items[0].Freshness);
        var unknown = await Validator(db, Fresh, null).ValidateAsync(TenantA, claim, CancellationToken.None);
        Assert.AreEqual(ClaimEvidenceCodes.FreshnessUnknown, unknown.Code, "no registry policy is fail-closed");
    }

    [TestMethod]
    public async Task HistoricalEvidenceIsValidOnlyWithAnExplicitPolicyAndKeepsItsStateAndAsOfDate()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var validator = Validator(db, Aged, Policy);

        var historical = await validator.ValidateAsync(
            TenantA, Claim(true, new SourceFreshnessClaimPolicy(true, true), Cite(r1)), CancellationToken.None);
        Assert.IsTrue(historical.Valid, historical.Code);
        Assert.AreEqual(FreshnessState.Stale, historical.Items[0].Freshness, "the true state is retained, never relabelled current");
        Assert.IsNotNull(historical.Items[0].HistoricalAsOf);

        foreach (var policy in new[] { new SourceFreshnessClaimPolicy(true, false), new SourceFreshnessClaimPolicy(false, true) })
        {
            var rejected = await validator.ValidateAsync(TenantA, Claim(true, policy, Cite(r1)), CancellationToken.None);
            Assert.AreEqual(ClaimEvidenceCodes.Stale, rejected.Code, "an unlabeled non-current item is never valid");
            Assert.IsNull(rejected.Items[0].HistoricalAsOf);
        }
    }

    private sealed class UnevaluableFreshness : IEvidenceFreshnessService
    {
        public Task<EvidenceFreshnessResult> EvaluateAsync(
            Guid tenantId, string evidenceRef, SourceFreshnessClaimPolicy? claimPolicy, CancellationToken cancellationToken) =>
            Task.FromResult(new EvidenceFreshnessResult(EvidenceFreshnessOutcome.NotFound, null));
    }

    [TestMethod]
    public async Task ExistingEvidenceWhoseFreshnessCannotBeEvaluatedIsFreshnessUnknownNotNotFound()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var r1 = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);

        var result = await new EfClaimEvidenceValidationService(db, new UnevaluableFreshness())
            .ValidateAsync(TenantA, Claim(true, null, Cite(r1)), CancellationToken.None);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(ClaimEvidenceCodes.FreshnessUnknown, result.Code);
    }

    private sealed class RecordingValidator(bool valid) : IClaimEvidenceValidationService
    {
        public int Calls { get; private set; }
        public Guid Tenant { get; private set; }
        public ClaimEvidenceValidationRequest? Last { get; private set; }

        public Task<ClaimEvidenceValidationResult> ValidateAsync(Guid t, ClaimEvidenceValidationRequest r, CancellationToken ct)
        {
            Calls++;
            Tenant = t;
            Last = r;
            return Task.FromResult(valid
                ? new ClaimEvidenceValidationResult(ClaimEvidenceValidationOutcome.Validated, true, null, [])
                : new ClaimEvidenceValidationResult(ClaimEvidenceValidationOutcome.Validated, false, ClaimEvidenceCodes.Required, []));
        }
    }

    [TestMethod]
    public async Task EndpointRequiresAuthenticationAndTenantAndMapsTheVerdictToStatusCodes()
    {
        var ok = new RecordingValidator(true);
        using var factory = new ApiFactory(ok);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        const string Path = "/api/v1/tenant-claims/validate-evidence";
        var body = new { evidenceRequired = true, historicalEvidenceAllowed = true, historicalTimeContextPreserved = true, citations = Array.Empty<object>() };

        using (var anonymous = await client.PostAsJsonAsync(Path, body))
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-op", "operator").Token,
                     Sign("synthetic-x", "analyst", ["not-a-uuid"]),
                     Sign("synthetic-x", "analyst", [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var response = await Post(client, Path, bearer, body);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.AreEqual(0, ok.Calls, "no validation without a valid tenant");

        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);
        using (var valid = await Post(client, Path + "?tenant=" + TenantB, good, body))
            Assert.AreEqual(HttpStatusCode.OK, valid.StatusCode);
        Assert.AreEqual(TenantA, ok.Tenant, "the tenant is the signed claim, never a request parameter");
        Assert.IsTrue(ok.Last!.Policy!.HistoricalEvidenceAllowed && ok.Last.Policy.HistoricalTimeContextPreserved);

        using (var missing = await Post(client, Path, good, new { evidenceRequired = true }))
            Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);

        var callsBefore = ok.Calls;
        using (var nullItem = await Post(client, Path, good, new { evidenceRequired = true, citations = new object?[] { null } }))
            Assert.AreEqual(HttpStatusCode.BadRequest, nullItem.StatusCode);
        Assert.AreEqual(callsBefore, ok.Calls, "a null citation must be refused before validation");

        var rejecting = new RecordingValidator(false);
        using var rejectingFactory = new ApiFactory(rejecting);
        using var rejectingClient = rejectingFactory.CreateClient();
        using var rejected = await Post(rejectingClient, Path, good, body);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        StringAssert.Contains(await rejected.Content.ReadAsStringAsync(), ClaimEvidenceCodes.Required);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, string bearer, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
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

    private sealed class ApiFactory(IClaimEvidenceValidationService validator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0012");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                services.AddSingleton(validator);
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
        var verified = await new EfEvidenceVerificationService(db, new EchoVerifier(), new FixedTime(At.AddHours(2)))
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
        builder.Database += "_claimevidence";
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
