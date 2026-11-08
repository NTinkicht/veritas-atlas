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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-15 slice 3: server-derived report composition. SYNTHETIC data, validator stubbed, no database.</summary>
[TestClass]
public sealed class TenantAnswerReportComposeEndpointTests
{
    private const string SigningSecret = "answer-report-compose-test-only-key-not-for-production-01";
    private const string Path = "/api/v1/tenant-answers/compose";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly string RefCurrent = "ev1:" + new string('a', 64);
    private static readonly string RefStale = "ev1:" + new string('b', 64);
    private static readonly string RefUnknownFresh = "ev1:" + new string('c', 64);
    private static readonly string RefMissing = "ev1:" + new string('d', 64);

    private sealed class StubValidator : IClaimEvidenceValidationService
    {
        public List<(Guid Tenant, ClaimEvidenceValidationRequest Request)> Calls { get; } = [];

        public Task<ClaimEvidenceValidationResult> ValidateAsync(Guid t, ClaimEvidenceValidationRequest r, CancellationToken ct)
        {
            Calls.Add((t, r));
            var evidenceRef = r.Citations![0].EvidenceRef!;
            var historical = r.Policy!.HistoricalEvidenceAllowed;
            ClaimEvidenceItemResult item;
            if (evidenceRef == RefCurrent) item = new(evidenceRef, true, null, FreshnessState.Current, "2026-01-01T00:00:00Z", null);
            else if (evidenceRef == RefStale && historical && r.Policy.HistoricalTimeContextPreserved)
                item = new(evidenceRef, true, null, FreshnessState.Stale, "2026-01-01T00:00:00Z", "2020-01-01");
            else if (evidenceRef == RefStale) item = new(evidenceRef, false, ClaimEvidenceCodes.Stale, FreshnessState.Stale, "2026-01-01T00:00:00Z", null);
            else if (evidenceRef == RefUnknownFresh) item = new(evidenceRef, false, ClaimEvidenceCodes.FreshnessUnknown, FreshnessState.Unknown, "2026-01-01T00:00:00Z", null);
            else item = new(evidenceRef, false, ClaimEvidenceCodes.NotFound, null, null, null);
            return Task.FromResult(new ClaimEvidenceValidationResult(
                ClaimEvidenceValidationOutcome.Validated, item.Valid, item.Code, [item]));
        }
    }

    private static async Task<JsonElement> Ok(HttpClient client, string bearer, object body)
    {
        using var response = await Post(client, bearer, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string[] Codes(JsonElement r) =>
        r.GetProperty("blockers").EnumerateArray().Select(b => b.GetProperty("code").GetString()!).ToArray();

    private static object Claim(string id, string[] refs, bool current = true, string? asOf = null, string? text = "Synthetic claim text") => new
    {
        claimId = id, currentState = current, historicalAsOf = asOf, text,
        citations = refs.Select(r => new { evidenceRef = r, parserName = "p", parserVersion = "1" }).ToArray(),
    };

    [TestMethod]
    public async Task RefusesAnonymousWrongTenantAndWrongRoleBeforeAnyValidation()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var body = new { claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "verified" };
        using (var anonymous = await client.PostAsJsonAsync(Path, body))
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        foreach (var bearer in new[]
                 {
                     Sign("x", ["analyst"], ["not-a-uuid"]),
                     Sign("x", ["analyst", "admin"], [TenantA.ToString("D")]),
                     Sign("x", ["guest"], [TenantA.ToString("D")]),
                 })
        {
            using var response = await Post(client, bearer, body);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.AreEqual(0, stub.Calls.Count);
    }

    [TestMethod]
    public async Task RejectsMissingOrUnsafeClaimTextAndInvalidBodiesBeforeAnyValidation()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var good = Sign("x", ["analyst"], [TenantA.ToString("D")]);
        var cases = new object[]
        {
            new { },
            new { claims = new[] { Claim("c1", [RefCurrent], text: null) }, humanVerification = "verified" },
            new { claims = new[] { Claim("c1", [RefCurrent], text: "  ") }, humanVerification = "verified" },
            new { claims = new[] { Claim("c1", [RefCurrent], text: "bad\u0000text") }, humanVerification = "verified" },
            new { claims = new[] { Claim("c1", [RefCurrent], text: new string('x', 2001)) }, humanVerification = "verified" },
            new { claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "approved" },
        };
        foreach (var body in cases)
        {
            using var response = await Post(client, good, body);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, JsonSerializer.Serialize(body));
        }

        Assert.AreEqual(0, stub.Calls.Count);
    }

    [TestMethod]
    public async Task ComposesAReportOnlyWhenTheServeGatePassesAndUsesTheSignedTenant()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["Reviewer"], [TenantA.ToString("D")]);
        var contradictions = new[] { new { contradictionId = "k1", claimIds = new[] { "c1", "h1", "other" } } };

        var served = await Ok(client, bearer, new
        {
            tenantId = TenantB,
            claims = new[]
            {
                Claim("h1", [RefStale], current: false, asOf: "2020-01-01", text: "Older rule"),
                Claim("c1", [RefCurrent], text: "Current rule"),
            },
            humanVerification = "verified", contradictions, disclosedContradictionIds = new[] { "k1" },
        });
        Assert.AreEqual("serve", served.GetProperty("decision").GetString());
        Assert.AreEqual("caller_asserted", served.GetProperty("humanVerificationSource").GetString());
        var report = served.GetProperty("report");
        Assert.AreEqual("verified", report.GetProperty("humanVerification").GetString());
        var claims = report.GetProperty("claims").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(new[] { "c1", "h1" }, claims.Select(c => c.GetProperty("claimId").GetString()).ToArray());
        Assert.AreEqual("Current rule", claims[0].GetProperty("text").GetString());
        Assert.AreEqual("current", claims[0].GetProperty("temporalScope").GetString());
        Assert.AreEqual("historical", claims[1].GetProperty("temporalScope").GetString());
        Assert.AreEqual("2020-01-01", claims[1].GetProperty("historicalAsOf").GetString());
        Assert.IsTrue(claims[1].GetProperty("nonCurrentEvidence").GetBoolean());
        Assert.AreEqual("stale", claims[1].GetProperty("citations")[0].GetProperty("freshness").GetString());
        var k = report.GetProperty("contradictions")[0];
        Assert.AreEqual("k1", k.GetProperty("contradictionId").GetString());
        CollectionAssert.AreEqual(new[] { "c1", "h1" }, k.GetProperty("claimIds").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.IsTrue(stub.Calls.All(c => c.Tenant == TenantA), "the tenant is the signed claim, never the body");
        Assert.IsFalse(served.GetRawText().Contains(TenantB.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(served.GetRawText().Contains(TenantA.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task BlockedAnswersReturnBlockersAndNeverClaimText()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["analyst"], [TenantA.ToString("D")]);
        var secret = "SYNTHETIC-CLAIM-TEXT-MUST-NOT-LEAK";

        foreach (var (name, body) in new (string, object)[]
                 {
                     (ServeBlockers.CurrentStateNotCurrent, new { claims = new[] { Claim("c1", [RefStale], text: secret) }, humanVerification = "verified" }),
                     (ServeBlockers.CitationNotVerified, new { claims = new[] { Claim("c1", [RefMissing], text: secret) }, humanVerification = "verified" }),
                     (ServeBlockers.HumanVerificationMissing, new { claims = new[] { Claim("c1", [RefCurrent], text: secret) }, humanVerification = "unreviewed" }),
                     (ServeBlockers.HistoricalAsOfMissing, new { claims = new[] { Claim("h1", [RefStale], current: false, text: secret) }, humanVerification = "verified" }),
                     (ServeBlockers.ContradictionNotDisclosed, new
                     {
                         claims = new[] { Claim("c1", [RefCurrent], text: secret) }, humanVerification = "verified",
                         contradictions = new[] { new { contradictionId = "k1", claimIds = new[] { "c1" } } },
                     }),
                 })
        {
            var r = await Ok(client, bearer, body);
            Assert.AreEqual("block", r.GetProperty("decision").GetString(), name);
            Assert.IsTrue(Codes(r).Contains(name), name);
            Assert.AreEqual(JsonValueKind.Null, r.GetProperty("report").ValueKind, name);
            Assert.IsFalse(r.GetRawText().Contains(secret), name);
        }
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string bearer, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private static string Sign(string name, string[] roles, string[] tenants)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, name),
        };
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
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)), SecurityAlgorithms.HmacSha256)));
    }

    private sealed class ApiFactory(IClaimEvidenceValidationService validator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0015");
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
}
