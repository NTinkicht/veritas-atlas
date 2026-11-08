using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-19 (#244): operational safety proven over the real Program.cs
/// pipeline (Production environment, in-memory transport). The database is
/// deliberately unreachable (127.0.0.1:1) so readiness must fail closed while
/// liveness stays independent, and CORS must refuse origins that are not
/// explicitly configured.
/// </summary>
[TestClass]
public sealed class OperationalSafetyPipelineTests
{
    private const string SigningSecret = "ops-safety-test-only-key-must-not-be-used-in-production-0123";
    private const string AllowedOrigin = "https://app.synthetic-veritas.example";
    private const string OtherOrigin = "https://evil.synthetic-veritas.example";
    private const string SyntheticDbPassword = "synthetic-db-password-not-real-9f3a";

    [TestMethod]
    public async Task LivenessStaysOkWhileDatabaseIsUnreachable()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/health", "/health/live" })
        {
            using var response = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path);
        }
    }

    [TestMethod]
    public async Task ReadinessReturns503WithoutLeakingConnectionDetailsWhenDatabaseIsDown()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        // Positive control: the same host is live, so the 503 below is caused
        // by the unreachable database and not by a broken pipeline.
        using var live = await client.GetAsync("/health/live");
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync("/health/ready");
        var body = await ready.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        foreach (var secret in new[] { SyntheticDbPassword, "synthetic-db-user", "127.0.0.1", "Password" })
        {
            Assert.IsFalse(body.Contains(secret, StringComparison.OrdinalIgnoreCase),
                $"readiness body leaked '{secret}'");
            Assert.IsFalse(
                ready.Headers.Concat(ready.Content.Headers)
                    .Any(h => h.Value.Any(v => v.Contains(secret, StringComparison.OrdinalIgnoreCase))),
                $"readiness headers leaked '{secret}'");
        }
    }

    [TestMethod]
    public async Task ConfiguredOriginReceivesCorsHeaderAndOthersDoNot()
    {
        using var factory = new ApiFactory(allowedOrigin: AllowedOrigin);
        using var client = factory.CreateClient();

        // Positive control: the configured origin is granted.
        using var allowed = await Preflight(client, AllowedOrigin);
        Assert.IsTrue(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var granted));
        CollectionAssert.AreEqual(new[] { AllowedOrigin }, granted!.ToArray());

        using var denied = await Preflight(client, OtherOrigin);
        Assert.IsFalse(denied.Headers.Contains("Access-Control-Allow-Origin"),
            "a non-configured origin must not receive Access-Control-Allow-Origin");

        using var simple = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        simple.Headers.Add("Origin", OtherOrigin);
        using var simpleResponse = await client.SendAsync(simple);
        Assert.IsFalse(simpleResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [TestMethod]
    public async Task ProductionWithoutConfiguredOriginsGrantsNoOrigin()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        // Positive control: the endpoint itself answers the request.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", AllowedOrigin);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Origin"));

        using var preflight = await Preflight(client, AllowedOrigin);
        Assert.IsFalse(preflight.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static Task<HttpResponseMessage> Preflight(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return client.SendAsync(request);
    }

    private sealed class ApiFactory(string? allowedOrigin = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0003");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=synthetic-db-user;Password=" + SyntheticDbPassword
                + ";Timeout=2;Command Timeout=2");
            if (allowedOrigin is not null)
            {
                builder.UseSetting("Cors:AllowedOrigins:0", allowedOrigin);
            }
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IAccessTokenRevocationStore, InMemoryAccessTokenRevocationStore>());
        }
    }
}
