using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Controllers;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-02: exercise the real JWT bearer middleware, authorization filter,
/// and production AuthController.Me action over an in-memory HTTP transport.
/// Only synthetic tokens are used; no database, external listener or login.
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
        using var client = app.GetTestClient();

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
        using var client = app.GetTestClient();
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
        using var client = app.GetTestClient();
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

    private static async Task<WebApplication> StartApiAsync()
    {
        // Isolate just the production controller and authentication pipeline:
        // the real Program also starts database-backed services not needed here.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Jwt:Secret"] = SigningSecret
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<DevUserStore>();
        builder.Services.AddSingleton<ProductionBootstrapAuthenticator>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<LoginAttemptGuard>();
        builder.Services.AddSingleton<JwtTokenService>();
        builder.Services.AddScoped<AuthRequestContext>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = true;
                options.TokenValidationParameters = RuntimeSecurity.CreateTokenValidationParameters(
                    builder.Configuration, builder.Environment.EnvironmentName);
            });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
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
