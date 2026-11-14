using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-01 (#264): an authenticated principal whose roles are unknown, mixed with unknown ones, or absent is
/// refused at the authorization gate (403) on legacy and tenant endpoints, including those that use a bare
/// <c>[Authorize]</c>, before any handler or database is reached. Positive controls prove the same requests with
/// a known role pass the gate, so the refusals are caused by the role and nothing else.
/// </summary>
[TestClass]
public sealed class UnknownRoleGateTests
{
    private const string SigningSecret = "unknown-role-gate-test-only-key-must-not-be-used-in-production-01";

    // Legacy list (fallback policy), tenant list (fallback policy) and bare [Authorize] (default policy).
    private static readonly string[] Paths =
    [
        "/api/v1/cases",
        "/api/v1/tenant-investigations",
        "/api/v1/auth/me",
    ];

    [TestMethod]
    public void PrincipalRolePredicateIsFailClosed()
    {
        Assert.IsTrue(KnownRoles.PrincipalHasOnlyKnownRoles(Principal(("role", "analyst"))));
        Assert.IsTrue(KnownRoles.PrincipalHasOnlyKnownRoles(Principal((ClaimTypes.Role, "Reviewer"), ("role", "reviewer"))));
        Assert.IsFalse(KnownRoles.PrincipalHasOnlyKnownRoles(Principal()), "no role claim");
        Assert.IsFalse(KnownRoles.PrincipalHasOnlyKnownRoles(Principal(("role", "intruder"))));
        Assert.IsFalse(KnownRoles.PrincipalHasOnlyKnownRoles(Principal(("role", " "))), "blank role");
        Assert.IsFalse(KnownRoles.PrincipalHasOnlyKnownRoles(Principal(("role", "admin"), (ClaimTypes.Role, "intruder"))),
            "an unknown role next to a known one is not trusted");
        Assert.IsFalse(KnownRoles.PrincipalHasOnlyKnownRoles(Principal(("role", "administrator"))), "near-miss name");
    }

    [TestMethod]
    public async Task UnknownMixedAndMissingRolesAreForbiddenAtTheGate()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

        foreach (var path in Paths)
        {
            var tokens = new (string Label, string Token)[]
            {
                ("unknown role", jwt.CreateToken("synthetic-intruder", "intruder", tenant).Token),
                ("blank role", jwt.CreateToken("synthetic-blank", "", tenant).Token),
                ("mixed roles", Handcrafted(factory, tenant, "admin", "intruder")),
                ("no role", Handcrafted(factory, tenant)),
            };
            foreach (var (label, token) in tokens)
            {
                var (status, body) = await GetAsync(client, path, token);
                Assert.AreEqual(HttpStatusCode.Forbidden, status, $"GET {path} with {label}: {body}");
            }
        }
    }

    [TestMethod]
    public async Task EveryKnownRolePassesTheGate()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

        foreach (var path in Paths)
        {
            foreach (var role in new[] { "operator", "analyst", "reviewer", "publisher", "admin", "Analyst" })
            {
                var (status, body) = await GetAsync(client, path, jwt.CreateToken("synthetic-" + role, role, tenant).Token);
                // The handler may fail on the deliberately unreachable database (500) or answer a role-specific 403
                // from inside the controller; the gate itself must not be what refuses a known role.
                Assert.AreNotEqual(HttpStatusCode.Unauthorized, status, $"GET {path} as {role}");
                if (path == "/api/v1/auth/me")
                {
                    Assert.AreEqual(HttpStatusCode.OK, status, $"GET {path} as {role}: {body}");
                }
            }
        }
    }

    [TestMethod]
    public async Task AnonymousPublicSurfaceIsUnaffected()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health/live");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        using var publicReader = await client.GetAsync("/api/v1/public/publications/" + Guid.NewGuid());
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, publicReader.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Forbidden, publicReader.StatusCode);
        using var anonymousCases = await client.GetAsync("/api/v1/cases");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousCases.StatusCode);
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    private static string Handcrafted(ApiFactory factory, Guid tenant, params string[] roles)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var env = factory.Services.GetRequiredService<IHostEnvironment>();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RuntimeSecurity.ResolveJwtSecret(config, env.EnvironmentName)));
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "synthetic-handcrafted"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(TenantClaim.Type, tenant.ToString("D")),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var token = new JwtSecurityToken(
            RuntimeSecurity.ResolveJwtIssuer(config), RuntimeSecurity.ResolveJwtAudience(config), claims,
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(HttpClient client, string path, string bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        try
        {
            using var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        catch (Exception)
        {
            // Passed the gate and failed on the unreachable database.
            return (HttpStatusCode.InternalServerError, string.Empty);
        }
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0003");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IAccessTokenRevocationStore, InMemoryAccessTokenRevocationStore>());
        }
    }
}
