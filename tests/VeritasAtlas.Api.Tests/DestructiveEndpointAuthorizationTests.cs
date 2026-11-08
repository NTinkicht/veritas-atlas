using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// #237: the legacy destructive endpoints erase the workflow audit trail and the
/// scenario snapshot (file stores under the app base directory). Only the
/// <c>admin</c> role may call them; every other role, including an unknown one,
/// is refused with 403 before any store is touched.
/// </summary>
[TestClass]
public sealed class DestructiveEndpointAuthorizationTests
{
    private const string SigningSecret = "destructive-endpoint-test-only-key-not-for-production-0123";
    private const string AuditSentinel = "[{\"sentinel\":\"audit\"}]";
    private const string SnapshotSentinel = "{\"sentinel\":\"snapshot\"}";
    private static readonly string AuditPath = Path.Combine(AppContext.BaseDirectory, "workflow-audit-store.json");
    private static readonly string SnapshotPath = Path.Combine(AppContext.BaseDirectory, "scenario-snapshot.json");
    private static readonly string[] Endpoints = ["/api/v1/workflow-audit/clear", "/api/v1/persistence/reset"];

    [DataTestMethod]
    [DataRow("operator")]
    [DataRow("analyst")]
    [DataRow("reviewer")]
    [DataRow("publisher")]
    [DataRow("intruder")]
    public async Task NonAdminRolesAreRefusedAndStoresAreUntouched(string role)
    {
        foreach (var endpoint in Endpoints)
        {
            Seed();
            try
            {
                using var factory = new ApiFactory();
                using var client = factory.CreateClient();
                var status = await PostAsync(factory, client, endpoint, role);

                Assert.AreEqual(HttpStatusCode.Forbidden, status, $"{endpoint} as {role}");
                Assert.AreEqual(AuditSentinel, File.ReadAllText(AuditPath), $"{endpoint} as {role} touched the audit store");
                Assert.AreEqual(SnapshotSentinel, File.ReadAllText(SnapshotPath), $"{endpoint} as {role} touched the snapshot");
            }
            finally
            {
                Cleanup();
            }
        }
    }

    [TestMethod]
    public async Task AdminKeepsAccessAndClearsTheStores()
    {
        foreach (var endpoint in Endpoints)
        {
            Seed();
            try
            {
                using var factory = new ApiFactory();
                using var client = factory.CreateClient();
                var status = await PostAsync(factory, client, endpoint, "admin");

                Assert.AreEqual(HttpStatusCode.OK, status, endpoint);
                Assert.AreNotEqual(AuditSentinel, File.ReadAllText(AuditPath), $"{endpoint} did not clear the audit store");
            }
            finally
            {
                Cleanup();
            }
        }
    }

    private static async Task<HttpStatusCode> PostAsync(ApiFactory factory, HttpClient client, string path, string role)
    {
        var jwt = factory.Services.GetRequiredService<JwtTokenService>();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", jwt.CreateToken("synthetic-" + role, role, Guid.NewGuid()).Token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static void Seed()
    {
        File.WriteAllText(AuditPath, AuditSentinel);
        File.WriteAllText(SnapshotPath, SnapshotSentinel);
    }

    private static void Cleanup()
    {
        File.Delete(AuditPath);
        File.Delete(SnapshotPath);
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
