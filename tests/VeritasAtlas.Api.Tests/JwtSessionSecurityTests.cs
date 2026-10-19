using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-02 (#71): JWT secret/lifetime fail-closed configuration and
/// expired/tampered/unsigned bearer token negative vectors.
/// </summary>
[TestClass]
public sealed class JwtSessionSecurityTests
{
    private const string Secret = "jwt-session-security-test-only-secret-0123456789";
    private const string OtherSecret = "jwt-session-security-other-test-secret-9876543210";

    [TestMethod]
    [DataRow("Production")]
    [DataRow("Staging")]
    [DataRow("Development")]
    public void SecretShorterThan32BytesFailsClosed(string environment)
    {
        var configuration = Configuration(secret: new string('s', 31));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RuntimeSecurity.ResolveJwtSecret(configuration, environment));
    }

    [TestMethod]
    public void SecretOfExactly32BytesIsAccepted()
    {
        var secret = new string('s', 32);

        Assert.AreEqual(
            secret,
            RuntimeSecurity.ResolveJwtSecret(Configuration(secret: secret), "Production"));
    }

    [TestMethod]
    [DataRow("Production")]
    [DataRow("Staging")]
    public void DevelopmentSecretIsForbiddenOutsideDevelopment(string environment)
    {
        var configuration = Configuration(secret: RuntimeSecurity.DevelopmentJwtSecret);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RuntimeSecurity.ResolveJwtSecret(configuration, environment));
    }

    [TestMethod]
    public void UnconfiguredLifetimeUsesDefault()
    {
        Assert.AreEqual(
            TimeSpan.FromMinutes(RuntimeSecurity.DefaultJwtLifetimeMinutes),
            RuntimeSecurity.ResolveJwtLifetime(Configuration()));
    }

    [TestMethod]
    [DataRow("1", 1)]
    [DataRow("60", 60)]
    [DataRow("1440", 1440)]
    public void ValidLifetimeIsUsed(string configured, int expectedMinutes)
    {
        Assert.AreEqual(
            TimeSpan.FromMinutes(expectedMinutes),
            RuntimeSecurity.ResolveJwtLifetime(Configuration(expiresMinutes: configured)));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("1441")]
    [DataRow("525600")]
    [DataRow("abc")]
    [DataRow("60.5")]
    [DataRow(" 60")]
    public void InvalidLifetimeFailsClosed(string configured)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RuntimeSecurity.ResolveJwtLifetime(Configuration(expiresMinutes: configured)));
    }

    [TestMethod]
    public void IssuedTokenValidatesWithNameAndRole()
    {
        var configuration = Configuration(secret: Secret, expiresMinutes: "30");
        var (token, expiresAtUtc) = new JwtTokenService(configuration, new TestEnvironment("Production"))
            .CreateToken("analyst-fixture", "reviewer");

        var principal = Validate(token, configuration);

        Assert.AreEqual("analyst-fixture", principal.Identity?.Name);
        Assert.IsTrue(principal.IsInRole("reviewer"));
        var remaining = expiresAtUtc - DateTime.UtcNow;
        Assert.IsTrue(remaining > TimeSpan.FromMinutes(29) && remaining <= TimeSpan.FromMinutes(30));
    }

    [TestMethod]
    public void TokenExpiredByOneSecondIsRejected()
    {
        var token = Sign(Secret, expires: DateTime.UtcNow.AddSeconds(-1));

        AssertRejected(token);
    }

    [TestMethod]
    public void TamperedSignatureIsRejected()
    {
        var token = Sign(Secret, expires: DateTime.UtcNow.AddMinutes(5));
        var parts = token.Split('.');
        var tamperedClaims = Base64UrlEncoder.Encode(
            "{\"unique_name\":\"admin-fixture\",\"role\":\"admin\",\"exp\":4102444800," +
            "\"iss\":\"VeritasAtlas\",\"aud\":\"VeritasAtlasUsers\"}");

        AssertRejected($"{parts[0]}.{tamperedClaims}.{parts[2]}");
    }

    [TestMethod]
    public void TokenSignedWithAnotherKeyIsRejected()
    {
        AssertRejected(Sign(OtherSecret, expires: DateTime.UtcNow.AddMinutes(5)));
    }

    [TestMethod]
    public void WrongIssuerIsRejected()
    {
        AssertRejected(Sign(Secret, expires: DateTime.UtcNow.AddMinutes(5), issuer: "Elsewhere"));
    }

    [TestMethod]
    public void WrongAudienceIsRejected()
    {
        AssertRejected(Sign(Secret, expires: DateTime.UtcNow.AddMinutes(5), audience: "OtherUsers"));
    }

    [TestMethod]
    public void TokenWithoutExpiryIsRejected()
    {
        AssertRejected(Sign(Secret, expires: null));
    }

    [TestMethod]
    public void UnsignedAlgNoneTokenIsRejected()
    {
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncoder.Encode(
            "{\"unique_name\":\"admin-fixture\",\"role\":\"admin\",\"exp\":4102444800," +
            "\"iss\":\"VeritasAtlas\",\"aud\":\"VeritasAtlasUsers\"}");

        AssertRejected($"{header}.{payload}.");
    }

    [TestMethod]
    public void TokenSignedWithAnotherHmacAlgorithmIsRejected()
    {
        // Same key and claims, but HS512 instead of the pinned HS256.
        var longSecret = Secret + Secret;
        var configuration = Configuration(secret: longSecret);
        var token = Sign(
            longSecret,
            expires: DateTime.UtcNow.AddMinutes(5),
            algorithm: SecurityAlgorithms.HmacSha512);

        AssertRejected(token, configuration);
    }

    private static void AssertRejected(string token, IConfiguration? configuration = null)
    {
        configuration ??= Configuration(secret: Secret);
        try
        {
            Validate(token, configuration);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return;
        }

        Assert.Fail("Token was accepted but must be rejected.");
    }

    private static ClaimsPrincipal Validate(string token, IConfiguration configuration)
    {
        var parameters = RuntimeSecurity.CreateTokenValidationParameters(configuration, "Production");
        return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
    }

    private static string Sign(
        string secret,
        DateTime? expires,
        string issuer = RuntimeSecurity.DefaultJwtIssuer,
        string audience = RuntimeSecurity.DefaultJwtAudience,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            algorithm);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim(ClaimTypes.Name, "admin-fixture"), new Claim(ClaimTypes.Role, "admin")],
            notBefore: expires is { } e && e < now ? e.AddMinutes(-10) : null,
            expires: expires,
            signingCredentials: credentials);
        if (expires is null)
        {
            token.Payload.Remove(JwtRegisteredClaimNames.Exp);
        }

        return new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false }.WriteToken(token);
    }

    private static IConfiguration Configuration(string? secret = null, string? expiresMinutes = null)
    {
        var values = new Dictionary<string, string?>();
        if (secret is not null) values["Auth:Jwt:Secret"] = secret;
        if (expiresMinutes is not null) values["Auth:Jwt:ExpiresMinutes"] = expiresMinutes;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "VeritasAtlas.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
