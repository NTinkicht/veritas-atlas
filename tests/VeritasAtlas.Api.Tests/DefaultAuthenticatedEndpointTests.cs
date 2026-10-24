using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-02 (#77): the API denies by default. Boots the real
/// <c>Program.cs</c> over an in-memory transport, enumerates every mapped
/// endpoint and proves that only the explicit anonymous allow-list is reachable
/// without a valid bearer. A controller added later without
/// <c>[Authorize]</c> is covered by the fallback policy and by this test.
/// </summary>
[TestClass]
public sealed class DefaultAuthenticatedEndpointTests
{
    private const string SigningSecret = "default-auth-test-only-key-must-not-be-used-in-production-0123";

    /// <summary>Exact anonymous surface: "METHOD route-template" ("*" = any method).</summary>
    private static readonly string[] AnonymousAllowList =
    [
        "* health",
        "* health/live",
        "* health/ready",
        "GET api/v1/auth-diagnostics/public",
        "POST api/v1/auth/login",
    ];

    [TestMethod]
    public void AnonymousEndpointsAreExactlyTheAllowList()
    {
        using var factory = new ApiFactory();
        _ = factory.Server;

        var anonymous = Endpoints(factory)
            .Where(e => e.AllowsAnonymous)
            .SelectMany(e => e.Keys)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            AnonymousAllowList.Order(StringComparer.Ordinal).ToArray(),
            anonymous,
            "anonymous endpoints: " + string.Join(", ", anonymous));
    }

    [TestMethod]
    public async Task EveryOtherEndpointRejectsMissingAndInvalidBearer()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var valid = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-operator", "operator").Token;
        var tampered = TamperSignature(valid);

        var protectedEndpoints = Endpoints(factory).Where(e => !e.AllowsAnonymous).ToArray();
        // Guard against a vacuous pass if endpoint discovery breaks.
        Assert.IsTrue(protectedEndpoints.Length >= 40,
            $"expected the full controller surface, found {protectedEndpoints.Length}");

        var failures = new List<string>();
        foreach (var endpoint in protectedEndpoints)
        {
            foreach (var method in endpoint.Methods)
            {
                var path = "/" + RouteParameter.Replace(endpoint.Template, Guid.NewGuid().ToString());
                foreach (var (label, bearer) in new[] { ("no bearer", (string?)null), ("tampered bearer", tampered) })
                {
                    using var request = new HttpRequestMessage(new HttpMethod(method), path);
                    if (bearer is not null)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                    }
                    if (method is "POST" or "PUT" or "PATCH")
                    {
                        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                    }

                    using var response = await client.SendAsync(request);
                    if (response.StatusCode != HttpStatusCode.Unauthorized)
                    {
                        failures.Add($"{method} {path} with {label} -> {(int)response.StatusCode}");
                    }
                }
            }
        }

        Assert.AreEqual(0, failures.Count, "protected endpoints reachable anonymously:\n" + string.Join("\n", failures));
    }

    [TestMethod]
    public async Task FormerlyAnonymousEndpointAcceptsValidBearer()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var valid = factory.Services.GetRequiredService<JwtTokenService>()
            .CreateToken("synthetic-analyst", "analyst").Token;

        using (var anonymous = await client.GetAsync("/api/v1/workflow-validation/rules"))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/workflow-validation/rules");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", valid);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task LivenessStaysAnonymous()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static readonly Regex RouteParameter = new(@"\{[^}]+\}", RegexOptions.Compiled);

    private sealed record EndpointInfo(string Template, string[] Methods, bool AllowsAnonymous)
    {
        public IEnumerable<string> Keys => Methods.Select(m => $"{m} {Template}");
    }

    private static IReadOnlyList<EndpointInfo> Endpoints(WebApplicationFactory<Program> factory)
    {
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        return source.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => new EndpointInfo(
                (e.RoutePattern.RawText ?? string.Empty).Trim('/'),
                e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() is { Length: > 0 } methods
                    ? methods
                    : ["*"],
                e.Metadata.GetMetadata<IAllowAnonymous>() is not null))
            .ToArray();
    }

    private static string TamperSignature(string bearer)
    {
        var segments = bearer.Split('.');
        var signature = segments[2].ToCharArray();
        signature[4] = signature[4] == 'A' ? 'B' : 'A';
        return $"{segments[0]}.{segments[1]}.{new string(signature)}";
    }

    /// <summary>
    /// Real entry point in the Production environment with synthetic
    /// configuration only; the unreachable connection string is never opened
    /// because every request is rejected before any controller runs.
    /// </summary>
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0002");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
        }
    }
}
