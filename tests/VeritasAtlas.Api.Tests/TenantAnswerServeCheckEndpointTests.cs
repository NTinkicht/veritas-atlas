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

/// <summary>VER-P0-15 slice 2 (#220): server-derived serve preflight. SYNTHETIC data, validator stubbed, no database.</summary>
[TestClass]
public sealed class TenantAnswerServeCheckEndpointTests
{
    private const string SigningSecret = "answer-serve-check-test-only-key-not-for-production-01";
    private const string Path = "/api/v1/tenant-answers/serve-check";
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

    private static object Claim(string id, string[] refs, bool current = true, string? asOf = null) => new
    {
        claimId = id, currentState = current, historicalAsOf = asOf,
        citations = refs.Select(r => new { evidenceRef = r, parserName = "p", parserVersion = "1" }).ToArray(),
    };

    private static async Task<JsonElement> Ok(HttpClient client, string bearer, object body)
    {
        using var response = await Post(client, bearer, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string[] Codes(JsonElement r) =>
        r.GetProperty("blockers").EnumerateArray().Select(b => b.GetProperty("code").GetString()!).ToArray();

    [TestMethod]
    public async Task RefusesAnonymousWrongTenantAndWrongRoleBeforeAnyValidation()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var body = new { claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "verified" };

        using (var anonymous = await client.PostAsJsonAsync(Path, body))
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-op", "operator").Token,
                     Sign("x", ["analyst"], ["not-a-uuid"]),
                     Sign("x", ["analyst"], [TenantA.ToString("D"), TenantB.ToString("D")]),
                     Sign("x", ["analyst", "admin"], [TenantA.ToString("D")]),
                     Sign("x", ["guest"], [TenantA.ToString("D")]),
                     Sign("x", [], [TenantA.ToString("D")]),
                 })
        {
            using var response = await Post(client, bearer, body);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.AreEqual(0, stub.Calls.Count);
    }

    [TestMethod]
    public async Task RejectsInvalidBodiesWithAStableCodeBeforeAnyValidation()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var good = Sign("x", ["analyst"], [TenantA.ToString("D")]);
        var ok = Claim("c1", [RefCurrent]);
        var cases = new object?[]
        {
            new { },
            new { claims = new[] { ok } },
            new { claims = new[] { ok }, humanVerification = "approved" },
            new { claims = new[] { ok }, humanVerification = "1" },
            new { claims = Array.Empty<object>(), humanVerification = "verified" },
            new { claims = new object?[] { null }, humanVerification = "verified" },
            new { claims = new[] { ok, ok }, humanVerification = "verified" },
            new { claims = new[] { Claim(" ", [RefCurrent]) }, humanVerification = "verified" },
            new { claims = new[] { Claim("a\n", [RefCurrent]) }, humanVerification = "verified" },
            new { claims = new[] { Claim(new string('x', 501), [RefCurrent]) }, humanVerification = "verified" },
            new { claims = Enumerable.Range(0, 51).Select(i => Claim("c" + i, [])).ToArray(), humanVerification = "verified" },
            new { claims = new[] { Claim("c1", Enumerable.Range(0, 21).Select(_ => RefCurrent).ToArray()) }, humanVerification = "verified" },
            new
            {
                claims = Enumerable.Range(0, 11).Select(i => Claim("c" + i, Enumerable.Range(0, 20).Select(_ => RefCurrent).ToArray())).ToArray(),
                humanVerification = "verified",
            },
            new { claims = new[] { ok }, humanVerification = "verified", contradictions = new object?[] { null } },
            new { claims = new[] { ok }, humanVerification = "verified", contradictions = new[] { new { contradictionId = "k", claimIds = (string[]?)null } } },
            new { claims = new[] { ok }, humanVerification = "verified", disclosedContradictionIds = new[] { "" } },
        };
        foreach (var body in cases)
        {
            using var response = await Post(client, good, body!);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, JsonSerializer.Serialize(body));
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_serve_check_request");
        }

        Assert.AreEqual(0, stub.Calls.Count);
    }

    [TestMethod]
    public async Task ServesOnlyWhenEveryGateConditionHoldsAndTakesTheTenantFromTheToken()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["Reviewer"], [TenantA.ToString("D")]);

        var served = await Ok(client, bearer, new
        {
            tenantId = TenantB, claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "Verified",
        });
        Assert.AreEqual("serve", served.GetProperty("decision").GetString());
        Assert.AreEqual("caller_asserted", served.GetProperty("humanVerificationSource").GetString());
        Assert.AreEqual(0, Codes(served).Length);
        Assert.AreEqual(1, stub.Calls.Count);
        Assert.AreEqual(TenantA, stub.Calls[0].Tenant, "the tenant is the signed claim, never the body");
        Assert.IsTrue(stub.Calls[0].Request.EvidenceRequired);
        Assert.IsFalse(stub.Calls[0].Request.Policy!.HistoricalEvidenceAllowed, "current-state claims use the current-only policy");
        Assert.IsFalse(served.GetRawText().Contains(TenantB.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task BlocksStaleUnknownUnverifiedMissingAndUncitedClaims()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["analyst"], [TenantA.ToString("D")]);

        var stale = await Ok(client, bearer, new { claims = new[] { Claim("c1", [RefStale]) }, humanVerification = "verified" });
        Assert.AreEqual("block", stale.GetProperty("decision").GetString());
        CollectionAssert.AreEqual(new[] { ServeBlockers.CurrentStateNotCurrent }, Codes(stale));

        var unknown = await Ok(client, bearer, new { claims = new[] { Claim("c1", [RefUnknownFresh]) }, humanVerification = "verified" });
        CollectionAssert.AreEqual(new[] { ServeBlockers.CurrentStateNotCurrent }, Codes(unknown));

        var missing = await Ok(client, bearer, new { claims = new[] { Claim("c1", [RefMissing]) }, humanVerification = "verified" });
        Assert.IsTrue(Codes(missing).Contains(ServeBlockers.CitationNotVerified));

        var uncited = await Ok(client, bearer, new { claims = new[] { Claim("c1", []) }, humanVerification = "verified" });
        CollectionAssert.AreEqual(new[] { ServeBlockers.ClaimWithoutCitation }, Codes(uncited));

        var unreviewed = await Ok(client, bearer, new { claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "unreviewed" });
        CollectionAssert.AreEqual(new[] { ServeBlockers.HumanVerificationMissing }, Codes(unreviewed));
        var rejected = await Ok(client, bearer, new { claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "rejected" });
        Assert.AreEqual("block", rejected.GetProperty("decision").GetString());
    }

    [TestMethod]
    public async Task HistoricalClaimsNeedAnAsOfDateAndUseTheHistoricalPolicyOnlyWithOne()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["analyst"], [TenantA.ToString("D")]);

        var withDate = await Ok(client, bearer, new
        {
            claims = new[] { Claim("h1", [RefStale], current: false, asOf: "2020-01-01") }, humanVerification = "verified",
        });
        Assert.AreEqual("serve", withDate.GetProperty("decision").GetString());
        Assert.IsTrue(stub.Calls[0].Request.Policy!.HistoricalEvidenceAllowed);
        Assert.IsTrue(stub.Calls[0].Request.Policy!.HistoricalTimeContextPreserved);

        var withoutDate = await Ok(client, bearer, new
        {
            claims = new[] { Claim("h1", [RefStale], current: false) }, humanVerification = "verified",
        });
        Assert.AreEqual("block", withoutDate.GetProperty("decision").GetString());
        Assert.IsFalse(stub.Calls[1].Request.Policy!.HistoricalTimeContextPreserved);
        Assert.IsTrue(Codes(withoutDate).Contains(ServeBlockers.HistoricalAsOfMissing));
    }

    [TestMethod]
    public async Task HistoricalClaimWithAsOfDateStillBlocksWhenFreshnessValidationIsRejected()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["analyst"], [TenantA.ToString("D")]);

        var unknown = await Ok(client, bearer, new
        {
            claims = new[] { Claim("h1", [RefUnknownFresh], current: false, asOf: "2020-01-01") }, humanVerification = "verified",
        });
        Assert.AreEqual("block", unknown.GetProperty("decision").GetString());
        Assert.IsTrue(Codes(unknown).Contains(ServeBlockers.CitationNotVerified));
    }

    [TestMethod]
    public async Task BlocksUndisclosedContradictionsAndAcceptsDisclosedOnes()
    {
        var stub = new StubValidator();
        using var factory = new ApiFactory(stub);
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["analyst"], [TenantA.ToString("D")]);
        var contradictions = new[] { new { contradictionId = "k1", claimIds = new[] { "c1", "other" } } };

        var undisclosed = await Ok(client, bearer, new
        {
            claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "verified", contradictions,
        });
        CollectionAssert.AreEqual(new[] { ServeBlockers.ContradictionNotDisclosed }, Codes(undisclosed));

        var disclosed = await Ok(client, bearer, new
        {
            claims = new[] { Claim("c1", [RefCurrent]) }, humanVerification = "verified", contradictions,
            disclosedContradictionIds = new[] { "k1" },
        });
        Assert.AreEqual("serve", disclosed.GetProperty("decision").GetString());
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
