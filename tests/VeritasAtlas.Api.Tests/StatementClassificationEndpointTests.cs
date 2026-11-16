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
/// <summary>
/// VER-P0-11 slice 4: tenant-scoped, read-only statement classification over verified evidence. Database tests run
/// on a real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_stmtclass</c> database). All fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class StatementClassificationEndpointTests
{
    private const string SigningSecret = "statement-classification-test-only-key-not-for-production-01";
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

    private static EfStatementClassificationService Service(VeritasAtlasDbContext db, MutableTime time, SourceFreshnessPolicy? policy) =>
        new(db, new EfEvidenceFreshnessService(db, new FixedPolicies(policy), new NoProofRevisions(), time));

    private static async Task<(string Class, string? Element, bool HasSpan)> Classify(
        EfStatementClassificationService service, Guid tenant, string reference, string statement)
    {
        var r = await service.ClassifyAsync(tenant, reference, statement, CancellationToken.None);
        Assert.AreEqual(StatementClassificationOutcome.Ok, r.Outcome, statement);
        return (r.Classification!.Class!.Value.ToString(), r.Classification.SupportingElementId, r.Classification.Span is not null);
    }

    [TestMethod]
    public async Task EachClassIsReturnedOverVerifiedEvidenceAndNothingIsWritten()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var service = Service(db, new MutableTime(At.AddMinutes(30)), Policy);
        var before = (await db.TenantEvidenceChunks.CountAsync(), await db.TenantEvidenceVerifications.CountAsync());

        var quotation = await Classify(service, TenantA, verified, "The holder may not transfer it.");
        Assert.AreEqual("Quotation", quotation.Class);
        Assert.IsTrue(quotation.HasSpan);
        Assert.AreEqual("s1", quotation.Element);
        Assert.AreEqual("Inference", (await Classify(service, TenantA, verified, "Section alpha informative shall apply only")).Class);
        Assert.AreEqual("Unresolved", (await Classify(service, TenantA, verified, "Section alpha may apply to the holder")).Class);
        Assert.AreEqual("Unsupported", (await Classify(service, TenantA, verified, "Section beta shall apply to everyone")).Class);
        Assert.AreEqual(before, (await db.TenantEvidenceChunks.CountAsync(), await db.TenantEvidenceVerifications.CountAsync()));
    }

    [TestMethod]
    public async Task UnverifiedUnknownAndForeignReferencesAreOneUniformNotFoundWithAPositiveControl()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var unverified = await SeedAsync(db, TenantA, "r2", "beta", verify: false);
        var service = Service(db, new MutableTime(At.AddMinutes(30)), Policy);
        const string Statement = "The holder may not transfer it.";

        Assert.AreEqual("Quotation", (await Classify(service, TenantA, verified, Statement)).Class, "positive control");
        foreach (var (tenant, reference) in new[] { (TenantA, unverified), (TenantA, "ev1:unknown"), (TenantB, verified) })
        {
            var r = await service.ClassifyAsync(tenant, reference, Statement, CancellationToken.None);
            Assert.AreEqual(StatementClassificationOutcome.NotFound, r.Outcome, reference);
            Assert.IsNull(r.Classification);
            Assert.IsNull(r.Freshness);
        }
    }

    [TestMethod]
    public async Task InvalidStatementsAreRefusedAndFreshnessIsReEvaluated()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var time = new MutableTime(At.AddMinutes(30));
        var service = Service(db, time, Policy);

        foreach (var bad in new[] { "", " ", " padded", "a\nb", new string('x', StatementClassifierV1.MaxStatementLength + 1) })
            Assert.AreEqual(StatementClassificationOutcome.InvalidRequest,
                (await service.ClassifyAsync(TenantA, verified, bad, CancellationToken.None)).Outcome, bad.Length.ToString());
        Assert.AreEqual(StatementClassificationOutcome.InvalidRequest,
            (await service.ClassifyAsync(TenantA, "bad\nref", "x y z w", CancellationToken.None)).Outcome);

        var fresh = await service.ClassifyAsync(TenantA, verified, "The holder may not transfer it.", CancellationToken.None);
        Assert.AreEqual(FreshnessState.Current, fresh.Freshness!.State);
        time.Now = At.AddHours(3);
        var aged = await service.ClassifyAsync(TenantA, verified, "The holder may not transfer it.", CancellationToken.None);
        Assert.AreEqual(FreshnessState.Stale, aged.Freshness!.State, "an earlier CURRENT result is never reused");
        Assert.IsFalse(aged.Freshness.Eligible);
    }

    [TestMethod]
    public async Task ATamperedChunkRefusesTheClassification()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verified = await SeedAsync(db, TenantA, "r1", "alpha", verify: true);
        var service = Service(db, new MutableTime(At.AddMinutes(30)), Policy);
        const string Statement = "The holder may not transfer it.";
        Assert.AreEqual(StatementClassificationOutcome.Ok, (await service.ClassifyAsync(TenantA, verified, Statement, CancellationToken.None)).Outcome, "positive control");

        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("ALTER TABLE tenant_evidence_chunks DISABLE TRIGGER USER", conn);
            await cmd.ExecuteNonQueryAsync();
            await using var upd = new NpgsqlCommand("UPDATE tenant_evidence_chunks SET text = text || ' tampered'", conn);
            await upd.ExecuteNonQueryAsync();
        }

        var refused = await service.ClassifyAsync(TenantA, verified, Statement, CancellationToken.None);
        Assert.AreEqual(StatementClassificationOutcome.Refused, refused.Outcome);
        Assert.IsNull(refused.Classification);
    }

    private sealed class FixedResult(StatementClassificationServiceResult result) : IStatementClassificationService
    {
        public int Calls;
        public Guid? Tenant;
        public string? Ref;
        public string? Statement;

        public Task<StatementClassificationServiceResult> ClassifyAsync(Guid tenantId, string evidenceRef, string statement, CancellationToken ct)
        {
            Calls++;
            Tenant = tenantId;
            Ref = evidenceRef;
            Statement = statement;
            return Task.FromResult(result);
        }
    }

    private const string Path = "/api/v1/tenant-evidence/statement-classification";

    [TestMethod]
    public async Task EndpointRequiresAuthenticationAndAValidTenantAndTrustsOnlyTheClaim()
    {
        var fixedResult = new FixedResult(new(StatementClassificationOutcome.NotFound, null, null));
        using var factory = new ApiFactory(fixedResult);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        const string Body = "{\"evidenceRef\":\"ev1:abc\",\"statement\":\"x\"}";

        using (var anonymous = await client.PostAsync(Path, new StringContent(Body, Encoding.UTF8, "application/json")))
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-op", "operator").Token,
                     Sign("synthetic-x", "analyst", ["not-a-uuid"]),
                     Sign("synthetic-x", "analyst", [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var response = await Post(client, Body, bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "tenant_required");
        }

        Assert.AreEqual(0, fixedResult.Calls);
        using (var notFound = await Post(client, "{\"evidenceRef\":\"ev1:abc\",\"statement\":\"x\",\"tenant\":\"" + TenantB + "\"}",
                   Sign("synthetic-an", "analyst", [TenantA.ToString("D")])))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);
            StringAssert.Contains(await notFound.Content.ReadAsStringAsync(), "evidence_not_found");
        }
        Assert.AreEqual(TenantA, fixedResult.Tenant, "the tenant is the signed claim, never a request parameter");
        Assert.AreEqual("ev1:abc", fixedResult.Ref);
    }

    [TestMethod]
    public async Task EndpointMapsOutcomesPinsTheWireShapeAndNeverEchoesTheStatement()
    {
        var good = Sign("synthetic-an", "analyst", [TenantA.ToString("D")]);
        var span = new ClaimEvidenceSpan("ev1:abc", "s1", "deadbeef", 3, 15);
        var ok = new FixedResult(new(StatementClassificationOutcome.Ok,
            StatementClassification.Of(StatementClass.Quotation) with
            {
                Span = span, SupportingEvidenceRef = "ev1:abc", SupportingElementId = "s1"
            },
            new ClaimCandidateFreshness(FreshnessState.Stale, false, "age-window-exceeded", "2026-11-02T11:00:00Z")));
        using (var factory = new ApiFactory(ok))
        using (var client = factory.CreateClient())
        {
            using var response = await Post(client, "{\"evidenceRef\":\"ev1:abc\",\"statement\":\"It shall apply.\"}", good);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = body.RootElement;
            CollectionAssert.AreEquivalent(
                new[] { "evidenceRef", "classifierName", "classifierVersion", "class", "supportingEvidenceRef", "supportingElementId", "span", "answerable", "freshness" },
                root.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.AreEqual("quotation", root.GetProperty("class").GetString());
            Assert.IsFalse(root.GetProperty("answerable").GetBoolean());
            Assert.AreEqual("stale", root.GetProperty("freshness").GetProperty("state").GetString());
            CollectionAssert.AreEquivalent(
                new[] { "elementId", "start", "length", "chunkTextSha256" },
                root.GetProperty("span").EnumerateObject().Select(p => p.Name).ToArray());
        }

        const string Secret = "SYNTHETIC-STATEMENT-MARKER";
        foreach (var (outcome, payload, expected, code) in new[]
                 {
                     (StatementClassificationOutcome.InvalidRequest, "{\"evidenceRef\":\"ev1:abc\",\"statement\":\"" + Secret + "\"}", HttpStatusCode.BadRequest, "invalid_statement_request"),
                     (StatementClassificationOutcome.InvalidRequest, "{\"statement\":\"" + Secret + "\"}", HttpStatusCode.BadRequest, "invalid_statement_request"),
                     (StatementClassificationOutcome.InvalidRequest, "{\"evidenceRef\":\"ev1:abc\"}", HttpStatusCode.BadRequest, "invalid_statement_request"),
                     (StatementClassificationOutcome.Refused, "{\"evidenceRef\":\"ev1:abc\",\"statement\":\"" + Secret + "\"}", HttpStatusCode.Conflict, "classification_refused"),
                 })
        {
            using var factory = new ApiFactory(new FixedResult(new(outcome, null, null)));
            using var client = factory.CreateClient();
            using var response = await Post(client, payload, good);
            Assert.AreEqual(expected, response.StatusCode, payload);
            var text = await response.Content.ReadAsStringAsync();
            StringAssert.Contains(text, code);
            Assert.IsFalse(text.Contains(Secret, StringComparison.Ordinal), "the statement is never echoed");
        }
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string json, string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
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

    private sealed class ApiFactory(IStatementClassificationService classifier) : WebApplicationFactory<Program>
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
                services.AddSingleton(classifier);
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
        builder.Database += "_stmtclass";
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
