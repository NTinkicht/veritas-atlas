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
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-02 (#79, owner decision A): server-revocable logout. Boots the real
/// <c>Program.cs</c> in Production with synthetic configuration only.
/// </summary>
[TestClass]
public sealed class AccessTokenRevocationTests
{
    private const string SigningSecret = "revocation-test-only-key-must-not-be-used-in-production-0123";
    private const string Me = "/api/v1/auth/me";
    private const string Rules = "/api/v1/workflow-validation/rules";
    private const string Logout = "/api/v1/auth/logout";

    [TestMethod]
    public void IssuedTokensCarryDistinctRandomJti()
    {
        using var factory = new ApiFactory();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        var first = Jti(tokens.CreateToken("synthetic-analyst", "analyst").Token);
        var second = Jti(tokens.CreateToken("synthetic-analyst", "analyst").Token);

        StringAssert.Matches(first, new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}$"));
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public async Task LogoutRevokesTheBearerOnEveryProtectedEndpointAndReplayIsRejected()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var (bearer, expiresAtUtc) = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-operator", "operator");

        Assert.AreEqual(HttpStatusCode.OK, await Send(client, HttpMethod.Get, Me, bearer));
        Assert.AreEqual(HttpStatusCode.OK, await Send(client, HttpMethod.Get, Rules, bearer));

        Assert.AreEqual(HttpStatusCode.NoContent, await Send(client, HttpMethod.Post, Logout, bearer));

        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Me, bearer));
        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Rules, bearer));
        // Replaying the revoked bearer against logout itself is also refused.
        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Post, Logout, bearer));

        // Only the caller's own jti is recorded, with the token's own expiry.
        var revoked = factory.Store.Revoked.Single();
        Assert.AreEqual(Jti(bearer), revoked.Key);
        Assert.IsTrue((revoked.Value - expiresAtUtc).Duration() < TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task LogoutDoesNotRevokeOtherSessionsOrOtherUsers()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var loggedOut = tokens.CreateToken("synthetic-reviewer", "reviewer").Token;
        var sameUserOtherSession = tokens.CreateToken("synthetic-reviewer", "reviewer").Token;
        var otherUser = tokens.CreateToken("synthetic-admin", "admin").Token;

        Assert.AreEqual(HttpStatusCode.NoContent, await Send(client, HttpMethod.Post, Logout, loggedOut));

        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Me, loggedOut));
        Assert.AreEqual(HttpStatusCode.OK, await Send(client, HttpMethod.Get, Me, sameUserOtherSession));
        Assert.AreEqual(HttpStatusCode.OK, await Send(client, HttpMethod.Get, Me, otherUser));
    }

    [TestMethod]
    public async Task LogoutIgnoresATokenIdentifierSuppliedInTheRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var attacker = tokens.CreateToken("synthetic-analyst", "analyst").Token;
        var victim = tokens.CreateToken("synthetic-admin", "admin").Token;

        using var request = new HttpRequestMessage(HttpMethod.Post, Logout + "?jti=" + Jti(victim));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", attacker);
        request.Content = new StringContent(
            $"{{\"jti\":\"{Jti(victim)}\"}}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, await Send(client, HttpMethod.Get, Me, victim));
        Assert.AreEqual(Jti(attacker), factory.Store.Revoked.Single().Key);
    }

    [TestMethod]
    public async Task ValidlySignedTokenWithoutJtiIsRejected()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var withoutJti = Sign(jti: null);
        var blankJti = Sign(jti: " ");
        var oversizedJti = Sign(jti: new string('a', AccessTokenRevocation.MaximumJtiLength + 1));

        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Me, withoutJti));
        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Me, blankJti));
        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Me, oversizedJti));
        // Control: the same shape with a jti is accepted, so the rejections
        // above are caused by the missing identifier.
        Assert.AreEqual(HttpStatusCode.OK, await Send(client, HttpMethod.Get, Me, Sign(jti: "control-jti")));
    }

    [TestMethod]
    public async Task ExpiredTokenIsStillRejectedEvenIfNeverRevoked()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var expired = Sign(jti: "expired-jti", expires: DateTime.UtcNow.AddSeconds(-1));

        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Me, expired));
    }

    [TestMethod]
    public async Task UnavailableRevocationStoreFailsClosed()
    {
        // Real PostgreSQL store, pointed at an unreachable database.
        using var factory = new ApiFactory(useInMemoryStore: false);
        using var client = factory.CreateClient();
        var bearer = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-operator", "operator").Token;

        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Me, bearer));
        Assert.AreEqual(HttpStatusCode.Unauthorized, await Send(client, HttpMethod.Get, Rules, bearer));
    }

    [TestMethod]
    public void VerifierIsSha256HexOfJti()
    {
        Assert.AreEqual(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            EfAccessTokenRevocationStore.Verifier("abc"));
        Assert.ThrowsExactly<ArgumentException>(() => EfAccessTokenRevocationStore.Verifier(" "));
    }

    /// <summary>
    /// Runs against a real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> holds a
    /// connection string to a disposable database; inconclusive otherwise
    /// (the CI workflow has no PostgreSQL service).
    /// </summary>
    [TestMethod]
    public async Task PostgresStoreRevokesIdempotentlyAndStoresOnlyTheVerifier()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return;
        }

        var options = new DbContextOptionsBuilder<VeritasAtlasDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var setup = new VeritasAtlasDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();
        }

        var jti = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var expires = DateTime.UtcNow.AddMinutes(30);

        await using (var db = new VeritasAtlasDbContext(options))
        {
            var store = new EfAccessTokenRevocationStore(db, TimeProvider.System);
            Assert.IsFalse(await store.IsRevokedAsync(jti, CancellationToken.None));

            await store.RevokeAsync(jti, expires, CancellationToken.None);
            await store.RevokeAsync(jti, expires.AddMinutes(5), CancellationToken.None);

            Assert.IsTrue(await store.IsRevokedAsync(jti, CancellationToken.None));
            Assert.IsFalse(await store.IsRevokedAsync(jti + "0", CancellationToken.None));
        }

        // Concurrent logouts of the same token neither fail nor duplicate.
        var concurrentJti = Guid.NewGuid().ToString("N");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            await new EfAccessTokenRevocationStore(db, TimeProvider.System)
                .RevokeAsync(concurrentJti, expires, CancellationToken.None);
        }));

        await using (var check = new VeritasAtlasDbContext(options))
        {
            var rows = await check.RevokedAccessTokens.AsNoTracking().ToListAsync();
            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEquivalent(
                new[] { EfAccessTokenRevocationStore.Verifier(jti), EfAccessTokenRevocationStore.Verifier(concurrentJti) },
                rows.Select(r => r.JtiSha256).ToArray());
            Assert.IsFalse(rows.Any(r => r.JtiSha256 == jti || r.JtiSha256 == concurrentJti),
                "the raw jti must never be stored");
            var first = rows.Single(r => r.JtiSha256 == EfAccessTokenRevocationStore.Verifier(jti));
            Assert.IsTrue((first.ExpiresAtUtc - expires).Duration() < TimeSpan.FromMilliseconds(1),
                "a repeated revocation must not change the recorded expiry");

            // The schema itself refuses anything but a lowercase hex verifier.
            await Assert.ThrowsExactlyAsync<DbUpdateException>(async () =>
            {
                check.RevokedAccessTokens.Add(new RevokedAccessToken
                {
                    JtiSha256 = new string('Z', 64),
                    ExpiresAtUtc = expires,
                    RevokedAtUtc = DateTime.UtcNow
                });
                await check.SaveChangesAsync();
            });
        }
    }

    private static async Task<HttpStatusCode> Send(HttpClient client, HttpMethod method, string path, string bearer)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string Jti(string bearer) =>
        new JwtSecurityTokenHandler().ReadJwtToken(bearer).Id;

    private static string Sign(string? jti, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "synthetic-reviewer"),
            new(ClaimTypes.Role, "reviewer")
        };
        if (jti is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, jti));
        }

        var token = new JwtSecurityToken(
            issuer: RuntimeSecurity.DefaultJwtIssuer,
            audience: RuntimeSecurity.DefaultJwtAudience,
            claims: claims,
            notBefore: now.AddMinutes(-10),
            expires: expires ?? now.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class ApiFactory(bool useInMemoryStore = true) : WebApplicationFactory<Program>
    {
        public InMemoryAccessTokenRevocationStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0003");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            if (useInMemoryStore)
            {
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IAccessTokenRevocationStore>(Store));
            }
        }
    }
}
