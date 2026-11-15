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
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-07 (#274): admin read of a parse run's verification state. SYNTHETIC documents only. Every test needs
/// <c>VERITAS_TEST_POSTGRES</c> (own <c>*_verifyreadapi</c> database) because the endpoint reads the
/// retrieval, parse runs and fetch policy before it reaches the verification service.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantParseRunVerificationReadEndpointTests
{
    private const string SigningSecret = "verify-api-test-only-key-not-for-production-0274";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private const string Doc =
        "<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Synthetic section one.</P1><P1 id=\"s2\">Synthetic section two.</P1></Legislation>";

    private static string Path(Guid retrieval, Guid run) =>
        $"/api/v1/tenant-raw-retrievals/{retrieval}/parses/{run}/verification";

    private sealed record Seeded(Guid Source, Guid Retrieval, Guid Run);

    [TestMethod]
    public async Task NonAdminMissingTenantActorAndAnonymousCallersAreRefusedEvenForAVerifiedRun()
    {
        if (Database() is not { } cs) return;
        var seeded = await ResetAndSeedAsync(cs, TenantA, "gb-uk", "statute");
        using var factory = new ApiFactory(cs, new EchoVerifier());
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var admin = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        using (var verify = await Send(client, HttpMethod.Post, Path(seeded.Retrieval, seeded.Run), admin))
            Assert.AreEqual(HttpStatusCode.Created, verify.StatusCode);
        var tenant = new[] { TenantA.ToString("D") };

        var denied = new List<(string Label, string Bearer, string Code)>
        {
            ("operator", tokens.CreateToken("synthetic-op", "operator", TenantA).Token, "admin_required"),
            ("analyst", tokens.CreateToken("synthetic-an", "analyst", TenantA).Token, "admin_required"),
            ("reviewer", tokens.CreateToken("synthetic-rev", "reviewer", TenantA).Token, "admin_required"),
            ("publisher", tokens.CreateToken("synthetic-pub", "publisher", TenantA).Token, "admin_required"),
            ("no role", Sign("synthetic-none", [], tenant), ""),
            ("admin plus operator", Sign("synthetic-two", ["admin", "operator"], tenant), "admin_required"),
            ("no tenant", tokens.CreateToken("synthetic-admin", "admin").Token, "tenant_required"),
            ("two tenants", Sign("synthetic-admin", ["admin"], [TenantA.ToString("D"), TenantB.ToString("D")]), "tenant_required"),
            ("no actor", Sign(null, ["admin"], tenant), "actor_required"),
            ("control-char actor", Sign("a\u0001b", ["admin"], tenant), "actor_required"),
        };
        foreach (var (label, bearer, code) in denied)
        {
            using var response = await Send(client, HttpMethod.Get, Path(seeded.Retrieval, seeded.Run), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, label);
            // A principal with no role claim is refused by the global authorization gate (empty body).
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), code, label);
        }

        using var anonymous = await client.GetAsync(Path(seeded.Retrieval, seeded.Run));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Positive control: the tenant admin reads the same run.
        using var ok = await Send(client, HttpMethod.Get, Path(seeded.Retrieval, seeded.Run), admin);
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
    }

    [TestMethod]
    public async Task AdminReadsVerifiedStateMetadataOnlyAndAnUnverifiedRunIsNeverReportedVerified()
    {
        if (Database() is not { } cs) return;
        var seeded = await ResetAndSeedAsync(cs, TenantA, "gb-uk", "statute");
        using var factory = new ApiFactory(cs, new EchoVerifier());
        using var client = factory.CreateClient();
        var admin = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);

        using var before = await Send(client, HttpMethod.Get, Path(seeded.Retrieval, seeded.Run), admin);
        Assert.AreEqual(HttpStatusCode.NotFound, before.StatusCode);
        StringAssert.Contains(await before.Content.ReadAsStringAsync(), "verification_not_found");

        using var verify = await Send(client, HttpMethod.Post, Path(seeded.Retrieval, seeded.Run), admin);
        Assert.AreEqual(HttpStatusCode.Created, verify.StatusCode);

        using var after = await Send(client, HttpMethod.Get, Path(seeded.Retrieval, seeded.Run), admin);
        var text = await after.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, after.StatusCode, text);
        StringAssert.Contains(text, "\"status\":\"verified\"");
        StringAssert.Contains(text, "\"verificationId\":\"");
        StringAssert.Contains(text, "\"verifiedAtUtc\":\"");
        StringAssert.DoesNotMatch(text, new Regex("ev1:|Synthetic section|rawBytes|chunk"), "metadata only");

        await using var check = new VeritasAtlasDbContext(Options(cs));
        var row = await check.TenantEvidenceVerifications.AsNoTracking().SingleAsync();
        StringAssert.Contains(text, row.Id.ToString());
        Assert.AreEqual(1, await check.TenantEvidenceVerifications.CountAsync(), "the read writes nothing");
    }

    [TestMethod]
    public async Task UnknownForeignMismatchedAndEmptyIdsAnswerTheSame404ForAVerifiedRun()
    {
        if (Database() is not { } cs) return;
        var seeded = await ResetAndSeedAsync(cs, TenantA, "gb-uk", "statute");
        Seeded other;
        await using (var db = new VeritasAtlasDbContext(Options(cs)))
            other = await SeedAsync(db, TenantA, "r2", "gb-uk", "statute");
        using var factory = new ApiFactory(cs, new EchoVerifier());
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);
        using (var verify = await Send(client, HttpMethod.Post, Path(seeded.Retrieval, seeded.Run), adminA))
            Assert.AreEqual(HttpStatusCode.Created, verify.StatusCode);

        var attempts = new (string Label, string Path, string Bearer)[]
        {
            ("another tenant", Path(seeded.Retrieval, seeded.Run), adminB),
            ("unknown retrieval", Path(Guid.NewGuid(), seeded.Run), adminA),
            ("unknown run", Path(seeded.Retrieval, Guid.NewGuid()), adminA),
            ("run of another retrieval", Path(seeded.Retrieval, other.Run), adminA),
            ("empty retrieval", Path(Guid.Empty, seeded.Run), adminA),
            ("empty run", Path(seeded.Retrieval, Guid.Empty), adminA),
        };
        foreach (var (label, path, bearer) in attempts)
        {
            using var response = await Send(client, HttpMethod.Get, path, bearer);
            var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, label + text);
            StringAssert.Contains(text, "parse_run_not_found", label);
        }
    }

    private static async Task<Seeded> ResetAndSeedAsync(string cs, Guid tenant, string? jurisdiction, string? sourceType)
    {
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        await using var db = new VeritasAtlasDbContext(Options(cs));
        return await SeedAsync(db, tenant, "r1", jurisdiction, sourceType);
    }

    /// <summary>A parsed run with an authenticated gb-uk/statute attestation; the source policy is the variable.</summary>
    private static async Task<Seeded> SeedAsync(
        VeritasAtlasDbContext db, Guid tenant, string key, string? jurisdiction, string? sourceType)
    {
        var source = (await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None))!.Id;
        if (jurisdiction is not null)
        {
            var proposed = await new EfTenantSourceFetchPolicyRegistry(db, TimeProvider.System).ProposeAsync(
                tenant, source,
                new SourceFetchPolicy("v1", jurisdiction, sourceType!, ["www.legislation.gov.uk"], ["https"], [443], 3),
                true, "synthetic-seeder", CancellationToken.None);
            Assert.AreEqual(SourceFetchPolicyProposalOutcome.Created, proposed.Outcome, string.Join(",", proposed.Reasons));
        }

        var retrieval = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source, key, Uri, At, Encoding.UTF8.GetBytes(Doc)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, retrieval.Outcome);
        var retrievalId = retrieval.Record!.Id;
        var run = (await new EfEvidenceParseService(db, TimeProvider.System)
            .ParseAsync(tenant, retrievalId, "synthetic-seeder", CancellationToken.None)).Run!.Id;
        var attestation = await new EfTenantRetrievalAttestationLedger(db, TimeProvider.System).RecordAsync(
            tenant,
            new RetrievalAttestationInput(
                retrievalId, "k-" + key, "synthetic-retriever", "synthetic-publisher",
                PublisherBindingMechanism.SourceRegistry, "registry-entry-1",
                SourceAuthenticationResult.Authenticated, "gb-uk", AttestedSourceType.Statute, At.AddMinutes(5)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, attestation.Outcome);
        return new Seeded(source, retrievalId, run);
    }

    private static async Task<int> CountVerificationsAsync(string cs)
    {
        await using var db = new VeritasAtlasDbContext(Options(cs));
        return await db.TenantEvidenceVerifications.CountAsync();
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string bearer)
    {
        var request = new HttpRequestMessage(method, path);
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
        builder.Database += "_verifyreadapi";
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

    /// <summary>Test double standing in for the future trusted verifier: echoes the claim as verified.</summary>
    private sealed class EchoVerifier : IPublisherBindingVerifier
    {
        public Task<PublisherBindingVerification> VerifyAsync(AttestationClaim c, CancellationToken ct) =>
            Task.FromResult(new PublisherBindingVerification(
                c.PublisherIdentity, c.BindingMechanism, c.BindingReference, c.FinalUri, c.ContentSha256, true));
    }

    private sealed class ApiFactory(string connectionString, IPublisherBindingVerifier? verifier)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0274");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                if (verifier is not null) services.AddSingleton(verifier);
            });
        }
    }
}
