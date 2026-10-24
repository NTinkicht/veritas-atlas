using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-02: exercise the real API entry point (Program.cs), its JWT bearer
/// middleware, authorization filter and AuthController.Me action over an
/// in-memory HTTP transport. Only synthetic tokens are used; no database,
/// external listener or login.
/// </summary>
[TestClass]
public sealed class AuthMePipelineTests
{
    private const string SigningSecret = "http-me-test-only-key-must-not-be-used-in-production-012345";
    private const string WrongSecret = "different-http-me-test-only-key-never-used-for-real-users";

    [TestMethod]
    public async Task MissingBearerReturnsUnauthorized()
    {
        await using var app = await StartApiAsync();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("tampered-signature")]
    [DataRow("wrong-signing-key")]
    [DataRow("wrong-issuer")]
    [DataRow("wrong-audience")]
    [DataRow("unsigned")]
    public async Task InvalidBearerReturnsUnauthorizedThroughHttpPipeline(string vector)
    {
        await using var app = await StartApiAsync();
        using var client = app.CreateClient();
        var bearer = vector switch
        {
            "expired" => Sign(expired: true),
            "tampered-signature" => TamperSignature(Sign()),
            "wrong-signing-key" => Sign(secret: WrongSecret),
            "wrong-issuer" => Sign(issuer: "UntrustedIssuer"),
            "wrong-audience" => Sign(audience: "OtherAudience"),
            "unsigned" => RemoveSignatureAndSetAlgNone(Sign()),
            _ => throw new ArgumentOutOfRangeException(nameof(vector))
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, vector);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains(bearer, StringComparison.Ordinal), "response must not echo a rejected token");
        Assert.IsFalse(body.Contains(SigningSecret, StringComparison.Ordinal), "signing material must not leak");
    }

    [TestMethod]
    public async Task ValidSignedReviewerTokenReturnsExpectedIdentityThroughHttpPipeline()
    {
        await using var app = await StartApiAsync();
        using var client = app.CreateClient();
        var bearer = app.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-reviewer", "reviewer").Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var result = json.RootElement;
        Assert.AreEqual("synthetic-reviewer", result.GetProperty("user").GetString());
        Assert.AreEqual("reviewer", result.GetProperty("role").GetString());
        Assert.IsTrue(result.GetProperty("isAuthenticated").GetBoolean());
        Assert.IsTrue(result.GetProperty("claims").EnumerateArray()
            .Any(claim => claim.GetProperty("value").GetString() == "synthetic-reviewer"));
        Assert.IsFalse(body.Contains(bearer, StringComparison.Ordinal), "bearer must not appear in response");
        Assert.IsFalse(body.Contains(SigningSecret, StringComparison.Ordinal), "signing secret must not appear");
    }

    private static Task<ApiFactory> StartApiAsync()
    {
        var factory = new ApiFactory();
        // Building the client boots the host, so a startup failure surfaces here.
        _ = factory.Server;
        return Task.FromResult(factory);
    }

    /// <summary>
    /// Boots the real <c>apps/api/VeritasAtlas.Api/Program.cs</c> entry point
    /// (its services, middleware order, JWT bearer options and controllers) in
    /// the Production environment. Only configuration is supplied: a synthetic
    /// signing secret, synthetic bootstrap credentials and an unreachable
    /// connection string that the /auth/me path never opens.
    /// </summary>
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            // UseSetting (not ConfigureAppConfiguration) so values are visible
            // to the eager configuration reads Program performs before Build().
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0001");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
            // No database here: an in-memory revocation list stands in for the
            // PostgreSQL store (VER-P0-02, #79); without it every valid bearer
            // is rejected because the revocation check fails closed.
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IAccessTokenRevocationStore, InMemoryAccessTokenRevocationStore>());
        }
    }

    private static string Sign(
        string secret = SigningSecret,
        bool expired = false,
        string? issuer = null,
        string? audience = null)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer ?? RuntimeSecurity.DefaultJwtIssuer,
            audience: audience ?? RuntimeSecurity.DefaultJwtAudience,
            claims:
            [
                new Claim(ClaimTypes.Name, "synthetic-reviewer"),
                new Claim(ClaimTypes.Role, "reviewer")
            ],
            notBefore: now.AddMinutes(-10),
            expires: expired ? now.AddMinutes(-1) : now.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string TamperSignature(string bearer)
    {
        var segments = bearer.Split('.');
        var signature = segments[2].ToCharArray();
        signature[4] = signature[4] == 'A' ? 'B' : 'A';
        return $"{segments[0]}.{segments[1]}.{new string(signature)}";
    }

    private static string RemoveSignatureAndSetAlgNone(string bearer)
    {
        var segments = bearer.Split('.');
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        return $"{header}.{segments[1]}.";
    }
}
