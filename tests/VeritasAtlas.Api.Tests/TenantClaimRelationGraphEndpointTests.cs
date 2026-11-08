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

/// <summary>VER-P0-13 slice 3 (#216): HTTP view over the claim relation graph. SYNTHETIC data, no database.</summary>
[TestClass]
public sealed class TenantClaimRelationGraphEndpointTests
{
    private const string SigningSecret = "claim-graph-test-only-key-not-for-production-01";
    private const string Path = "/api/v1/tenant-claims/relation-graph";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static object C(string id, string value = "5", string? asOf = "2024-01-01", string? key = "ent-1",
        bool negated = false, string subject = "Acme Ltd") => new
    {
        claimId = id, evidenceRef = "ev1:" + id, sourceId = "src-" + id, subject, entityKey = key,
        predicate = "employees", negated, value, asOf,
    };

    private static async Task<JsonElement> Ok(HttpClient client, string bearer, object body)
    {
        using var response = await Post(client, bearer, body);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [TestMethod]
    public async Task RefusesAnonymousWrongTenantAndWrongRoleBeforeBuilding()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var body = new { claims = new[] { C("a") } };

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
        var tooMany = Enumerable.Range(0, 201).Select(i => C("c" + i)).ToArray();
        var cases = new object?[]
        {
            new { },
            new { claims = Array.Empty<object>() },
            new { claims = new object?[] { null } },
            new { claims = tooMany },
            new { claims = new[] { C("a"), C("a") } },
            new { claims = new[] { C("a", subject: " ") } },
            new { claims = new[] { C("a", value: new string('v', 501)) } },
            new { claims = new[] { C("a", value: "5\u0007") } },
            new { claims = new[] { C("a\n") } },
        };
        foreach (var body in cases)
        {
            using var response = await Post(client, good, body!);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_claim_graph_request");
        }
    }

    [TestMethod]
    public async Task MapsAgreeingConflictingSupersededUnresolvedAndCollisionCasesEndToEnd()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["Reviewer"], [TenantA.ToString("D")]);

        var conflict = await Ok(client, bearer, new { claims = new[] { C("a", "5"), C("b", "9") } });
        Assert.IsFalse(conflict.GetProperty("evidenceVerified").GetBoolean());
        var graph = conflict.GetProperty("graph");
        Assert.AreEqual(TenantA.ToString("D"), graph.GetProperty("tenantId").GetString());
        Assert.AreEqual(1, graph.GetProperty("edges").GetArrayLength());
        Assert.AreEqual("ev1:a", graph.GetProperty("edges")[0].GetProperty("firstEvidenceRef").GetString());
        Assert.AreEqual("src-b", graph.GetProperty("edges")[0].GetProperty("secondSourceId").GetString());

        var agreeing = await Ok(client, bearer, new { claims = new[] { C("a"), C("b") } });
        Assert.AreEqual(2, agreeing.GetProperty("graph").GetProperty("nodes").GetArrayLength());
        StringAssert.Contains(agreeing.GetProperty("graph").GetProperty("edges")[0].GetRawText(), "same_value_same_polarity");

        var superseded = await Ok(client, bearer, new { claims = new[] { C("new", "9", "2025-01-01"), C("old", "5", "2024-01-01") } });
        Assert.AreEqual("old", superseded.GetProperty("graph").GetProperty("edges")[0].GetProperty("supersededClaimId").GetString());

        var unresolved = await Ok(client, bearer, new { claims = new[] { C("a", "5", key: null), C("b", "9") } });
        StringAssert.Contains(unresolved.GetProperty("graph").GetProperty("edges")[0].GetRawText(), "entity_unresolved");

        var collision = await Ok(client, bearer, new { claims = new[] { C("a", "5", key: "ent-1"), C("b", "9", key: "ent-2") } });
        Assert.AreEqual(0, collision.GetProperty("graph").GetProperty("edges").GetArrayLength());
    }

    [TestMethod]
    public async Task IgnoresACallerSuppliedTenantAndOutputIsIndependentOfInputOrder()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var bearer = Sign("x", ["analyst"], [TenantA.ToString("D")]);
        var forged = await Ok(client, bearer, new { tenantId = TenantB, claims = new[] { C("a"), C("b", "9") } });
        Assert.AreEqual(TenantA.ToString("D"), forged.GetProperty("graph").GetProperty("tenantId").GetString());

        var swapped = await Ok(client, bearer, new { claims = new[] { C("b", "9"), C("a") } });
        Assert.AreEqual(forged.GetProperty("graph").GetRawText(), swapped.GetProperty("graph").GetRawText());
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
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0013");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore()));
        }
    }
}
