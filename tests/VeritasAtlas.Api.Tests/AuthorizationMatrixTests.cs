using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-01 (#236): role-by-endpoint authorization matrix. Role checks are
/// imperative inside controllers, so this test boots the real
/// <c>Program.cs</c>, calls every protected endpoint with a valid token for
/// each role (and for an unknown role) and pins exactly which pairs are denied
/// with 403. Widening or narrowing a role set, or adding an endpoint, fails
/// here until <c>docs/architecture/AUTHORIZATION_MATRIX.md</c> and the pinned
/// matrix are updated together.
/// </summary>
[TestClass]
public sealed class AuthorizationMatrixTests
{
    private const string SigningSecret = "authz-matrix-test-only-key-must-not-be-used-in-production-0123";
    private static readonly string[] Roles = ["operator", "analyst", "reviewer", "publisher", "admin"];
    private static readonly string[] AllRoles = [.. Roles, UnknownRole];
    private const string UnknownRole = "intruder";
    private static readonly Regex RouteParameter = new(@"\{[^}]+\}", RegexOptions.Compiled);

    [TestMethod]
    public async Task RoleByEndpointMatrixIsPinned()
    {
        var matrix = await ProbeAsync(includeUnknown: true);
        Assert.IsTrue(matrix.Count >= 40, $"expected the full controller surface, found {matrix.Count}");

        var actual = matrix
            .Select(row => $"{row.Key} | denied: " + string.Join(",", AllRoles.Where(r => row.Value[r] == HttpStatusCode.Forbidden)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var pinned = PinnedMatrix.Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(pinned, actual,
            "authorization matrix drifted; update the pinned matrix and docs/architecture/AUTHORIZATION_MATRIX.md together:\n"
            + string.Join("\n", actual));
    }

    /// <summary>
    /// "METHOD route | denied: roles answered with 403". An empty list means no
    /// role is denied by the authorization gate. A 400 produced by model
    /// binding runs before in-action role checks, so endpoints that answer 400
    /// to the empty probe body are pinned as not denied; their role gating is
    /// not asserted here (tracked as a documented limitation).
    /// </summary>
    private static readonly string[] PinnedMatrix =
    [
        "DELETE api/v1/tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/bookmark | denied: reviewer,publisher,admin,intruder",
        "GET api/v1/agent-runs | denied: ",
        "GET api/v1/auth-diagnostics/protected | denied: ",
        "GET api/v1/auth/me | denied: ",
        "GET api/v1/cases | denied: ",
        "GET api/v1/cases/status-counts | denied: ",
        "GET api/v1/cases/{id:guid} | denied: ",
        "GET api/v1/claims | denied: ",
        "GET api/v1/claims/{id:guid} | denied: ",
        "GET api/v1/contradictions | denied: ",
        "GET api/v1/contradictions/{id:guid} | denied: ",
        "GET api/v1/documents | denied: ",
        "GET api/v1/documents/{id:guid} | denied: ",
        "GET api/v1/evidence | denied: ",
        "GET api/v1/evidence/{id:guid} | denied: ",
        "GET api/v1/persistence/snapshot | denied: ",
        "GET api/v1/persons | denied: ",
        "GET api/v1/persons/{id:guid} | denied: ",
        "GET api/v1/reviews | denied: ",
        "GET api/v1/sources | denied: ",
        "GET api/v1/sources/{id:guid} | denied: ",
        "GET api/v1/statements | denied: ",
        "GET api/v1/statements/{id:guid} | denied: ",
        "GET api/v1/tenant-evidence/claim-candidates | denied: ",
        "GET api/v1/tenant-evidence/search | denied: ",
        "GET api/v1/tenant-investigations | denied: intruder",
        "GET api/v1/tenant-investigations/{id:guid} | denied: intruder",
        "GET api/v1/tenant-investigations/{investigationId:guid}/bookmarks | denied: intruder",
        "GET api/v1/tenant-investigations/{investigationId:guid}/evidence-items | denied: intruder",
        "GET api/v1/tenant-investigations/{investigationId:guid}/evidence-items/comparison | denied: intruder",
        "GET api/v1/tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/annotations | denied: intruder",
        "GET api/v1/tenant-publication-revisions/{id:guid} | denied: admin,intruder",
        "GET api/v1/tenant-sources | denied: ",
        "GET api/v1/tenant-sources/{id:guid} | denied: ",
        "GET api/v1/tenant-sources/{id:guid}/fetch-policy | denied: ",
        "GET api/v1/workflow-audit/entries | denied: ",
        "GET api/v1/workflow-audit/rules | denied: ",
        "GET api/v1/workflow-diagnostics/routes | denied: ",
        "GET api/v1/workflow-diagnostics/summary | denied: ",
        "GET api/v1/workflow-validation/rules | denied: ",
        "POST api/v1/actions/cases/{caseId:guid}/approve | denied: ",
        "POST api/v1/actions/cases/{caseId:guid}/hold | denied: ",
        "POST api/v1/actions/cases/{caseId:guid}/prepare | denied: ",
        "POST api/v1/actions/cases/{caseId:guid}/publish | denied: ",
        "POST api/v1/actions/cases/{caseId:guid}/reject | denied: ",
        "POST api/v1/actions/cases/{caseId:guid}/submit | denied: ",
        "POST api/v1/actions/cases/{caseId}/approve | denied: ",
        "POST api/v1/actions/cases/{caseId}/reject | denied: ",
        "POST api/v1/actions/cases/{caseId}/submit | denied: ",
        "POST api/v1/actions/contradictions/{id}/resolve | denied: ",
        "POST api/v1/actions/reviews/{id}/complete | denied: ",
        "POST api/v1/actions/seed/lifecycle | denied: analyst,intruder",
        "POST api/v1/agent-runs/start | denied: ",
        "POST api/v1/agent-runs/{id:guid}/complete | denied: ",
        "POST api/v1/agent-runs/{id:guid}/fail | denied: ",
        "POST api/v1/auth/logout | denied: ",
        "POST api/v1/cases | denied: ",
        "POST api/v1/cases/{id:guid}/approve | denied: ",
        "POST api/v1/cases/{id:guid}/claims | denied: ",
        "POST api/v1/cases/{id:guid}/reject | denied: ",
        "POST api/v1/claims | denied: ",
        "POST api/v1/contradictions | denied: ",
        "POST api/v1/documents | denied: ",
        "POST api/v1/evidence | denied: ",
        "POST api/v1/persistence/reset | denied: operator,analyst,reviewer,publisher,intruder",
        "POST api/v1/persons | denied: ",
        "POST api/v1/persons/{id:guid}/aliases | denied: ",
        "POST api/v1/publication-workflow/cases/{caseId}/hold | denied: ",
        "POST api/v1/publication-workflow/cases/{caseId}/prepare | denied: ",
        "POST api/v1/publication-workflow/cases/{caseId}/publish | denied: ",
        "POST api/v1/review-workflow/claims/{claimId}/return-for-edit | denied: ",
        "POST api/v1/review-workflow/claims/{claimId}/send-to-review | denied: ",
        "POST api/v1/review-workflow/contradictions/{id}/escalate | denied: ",
        "POST api/v1/review-workflow/reviews/{id}/reopen | denied: ",
        "POST api/v1/reviews | denied: ",
        "POST api/v1/reviews/{id:guid}/complete | denied: ",
        "POST api/v1/sources | denied: ",
        "POST api/v1/statements | denied: ",
        "POST api/v1/tenant-answers/compose | denied: intruder",
        "POST api/v1/tenant-answers/serve-check | denied: intruder",
        "POST api/v1/tenant-claims/confidence | denied: intruder",
        "POST api/v1/tenant-claims/relation-graph | denied: intruder",
        "POST api/v1/tenant-claims/validate-evidence | denied: ",
        "POST api/v1/tenant-investigations | denied: reviewer,publisher,admin,intruder",
        "POST api/v1/tenant-investigations/{investigationId:guid}/evidence-items | denied: reviewer,publisher,admin,intruder",
        "POST api/v1/tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/annotations | denied: reviewer,publisher,admin,intruder",
        "POST api/v1/tenant-publication-revisions | denied: reviewer,publisher,admin,intruder",
        "POST api/v1/tenant-publication-revisions/{id:guid}/transitions | denied: admin,intruder",
        "POST api/v1/tenant-sources | denied: operator,analyst,reviewer,publisher,intruder",
        "POST api/v1/workflow-audit/clear | denied: operator,analyst,reviewer,publisher,intruder",
        "PUT api/v1/tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/bookmark | denied: reviewer,publisher,admin,intruder",
        "PUT api/v1/tenant-publication-revisions/{id:guid}/visibility | denied: operator,analyst,reviewer,admin,intruder",
        "PUT api/v1/tenant-sources/{id:guid}/fetch-policy | denied: operator,analyst,reviewer,publisher,intruder"
    ];

    private static async Task<SortedDictionary<string, Dictionary<string, HttpStatusCode>>> ProbeAsync(bool includeUnknown)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var jwt = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var roles = includeUnknown ? Roles.Append(UnknownRole).ToArray() : Roles;

        var result = new SortedDictionary<string, Dictionary<string, HttpStatusCode>>(StringComparer.Ordinal);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>() is { HttpMethods.Count: > 0 });

        foreach (var endpoint in endpoints)
        {
            var template = (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/');
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)
            {
                var path = "/" + RouteParameter.Replace(template, Guid.Empty.ToString());
                var row = new Dictionary<string, HttpStatusCode>();
                foreach (var role in roles)
                {
                    using var request = new HttpRequestMessage(new HttpMethod(method), path);
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        // Fresh token per request: logout revokes its jti.
                        jwt.CreateToken("synthetic-" + role, role, tenant).Token);
                    if (method is "POST" or "PUT" or "PATCH")
                    {
                        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    }
                    var status = await SendAsync(client, request);
                    Assert.AreNotEqual(HttpStatusCode.Unauthorized, status, $"{method} {template} as {role}");
                    row[role] = status;
                }
                result[$"{method} {template}"] = row;
            }
        }

        return result;
    }

    /// <summary>
    /// A role that passes the authorization gate reaches the handler, which
    /// fails on the deliberately unreachable database. That failure surfaces as
    /// an exception from the in-memory server and counts as "not denied".
    /// </summary>
    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        try
        {
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        catch (Exception)
        {
            return HttpStatusCode.InternalServerError;
        }
    }

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
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IAccessTokenRevocationStore, InMemoryAccessTokenRevocationStore>());
        }
    }
}
