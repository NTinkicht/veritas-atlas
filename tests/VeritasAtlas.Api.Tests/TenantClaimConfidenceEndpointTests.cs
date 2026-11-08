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
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-14 slice 2 (#218): HTTP view over the confidence labeler. SYNTHETIC data, no database.</summary>
[TestClass]
public sealed class TenantClaimConfidenceEndpointTests
{
    private const string SigningSecret = "claim-confidence-test-only-key-not-for-production-01";
    private const string Path = "/api/v1/tenant-claims/confidence";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static object I(string id, int? sup = 2, int? ind = 2, int? con = 0, int? sps = 0, int? stl = 0, int? unk = 0, int? unr = 0) => new
    {
        claimId = id, supportingEvidenceCount = sup, independentSourceCount = ind, conflictingCount = con,
        supersededCount = sps, staleCount = stl, unknownFreshnessCount = unk, unresolvedRelationCount = unr,
    };

    private static async Task<JsonElement> Ok(HttpClient client, string bearer, object body)
    {
        using var response = await Post(client, bearer, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [TestMethod]
    public async Task RefusesAnonymousWrongTenantAndWrongRoleBeforeLabeling()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var body = new { claims = new[] { I("a") } };

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
    }

    [TestMethod]
    public async Task RejectsInvalidBodiesWithAStableCode()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var good = Sign("x", ["analyst"], [TenantA.ToString("D")]);
        var cases = new object?[]
        {
            new { },
            new { claims = Array.Empty<object>() },
            new { claims = new object?[] { null } },
            new { claims = Enumerable.Range(0, 201).Select(i => I("c" + i)).ToArray() },
            new { claims = new[] { I("a"), I("a") } },
            new { claims = new[] { I(" ") } },
            new { claims = new[] { I(new string('x', 501)) } },
            new { claims = new[] { I("a\n") } },
            new { claims = new[] { I("a", sup: -1) } },
            new { claims = new[] { I("a", unr: 100_001) } },
        };
        foreach (var body in cases)
        {
            using var response = await Post(client, good, body!);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_confidence_request");
        }
    }

    [TestMethod]
    public async Task MapsSignalsToBandsAndReasonsInRequestOrderWithoutClaimingVerification()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["Reviewer"], [TenantA.ToString("D")]);

        var result = await Ok(client, bearer, new
        {
            claims = new[]
            {
                I("clean"), I("single", ind: 1), I("missing", unk: null), I("none", sup: 0, ind: 0),
                I("conflict", con: 1), I("stale", stl: 1),
            },
        });
        Assert.IsFalse(result.GetProperty("evidenceVerified").GetBoolean());
        var labels = result.GetProperty("labels");
        var expected = new[]
        {
            ("clean", "high", "multiple_independent_sources"), ("single", "medium", "single_independent_source"),
            ("missing", "insufficient", "input_missing"), ("none", "insufficient", "no_support"),
            ("conflict", "low", "conflicting_evidence"), ("stale", "medium", "stale_evidence"),
        };
        Assert.AreEqual(expected.Length, labels.GetArrayLength());
        for (var n = 0; n < expected.Length; n++)
        {
            Assert.AreEqual(expected[n].Item1, labels[n].GetProperty("claimId").GetString());
            Assert.AreEqual(expected[n].Item2, labels[n].GetProperty("band").GetString());
            StringAssert.Contains(labels[n].GetProperty("reasons").GetRawText(), expected[n].Item3);
            Assert.IsFalse(labels[n].TryGetProperty("score", out _));
        }
    }

    [TestMethod]
    public async Task OmittedCountsAreNeverDefaultedAndACallerSuppliedTenantIsIgnored()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["analyst"], [TenantA.ToString("D")]);
        var omitted = await Ok(client, bearer, new { tenantId = TenantB, claims = new[] { new { claimId = "a" } } });
        Assert.AreEqual("insufficient", omitted.GetProperty("labels")[0].GetProperty("band").GetString());
        StringAssert.Contains(omitted.GetProperty("labels")[0].GetProperty("reasons").GetRawText(), "input_missing");
        Assert.IsFalse(omitted.GetRawText().Contains(TenantB.ToString("D"), StringComparison.OrdinalIgnoreCase));
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

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0014");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore()));
        }
    }
}
