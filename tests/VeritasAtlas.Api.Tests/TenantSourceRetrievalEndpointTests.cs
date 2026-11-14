using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Api.Controllers;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 (#262): the authorized retrieval trigger. SYNTHETIC hosts and bytes only; the fetcher is a
/// recording double or a real fetcher over a fake handler and resolver, so no test reaches a network.
/// Database-backed tests need <c>VERITAS_TEST_POSTGRES</c> (own <c>*_retrievalapi</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantSourceRetrievalEndpointTests
{
    private const string SigningSecret = "retrieval-api-test-only-key-not-for-production-01";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Path(Guid id) => $"/api/v1/tenant-sources/{id}/retrievals";

    private static object Body(string key = "attempt-1", string? pin = null) => new
    {
        attemptKey = key,
        requestedUri = "https://records.example.dz/doc/1",
        policyVersion = "2026-10.1",
        jurisdiction = "dz",
        sourceType = "court_record",
        pinnedContentSha256 = pin
    };

    [TestMethod]
    public async Task EveryNonAdminCallerIsRefusedBeforeTheFetcher()
    {
        var fetcher = new RecordingFetcher();
        using var factory = new ApiFactory(fetcher: fetcher);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = new[] { TenantA.ToString("D") };

        var bearers = new List<(string Label, string Bearer)>
        {
            ("operator", tokens.CreateToken("synthetic-op", "operator", TenantA).Token),
            ("reviewer", tokens.CreateToken("synthetic-rev", "reviewer", TenantA).Token),
            ("publisher", tokens.CreateToken("synthetic-pub", "publisher", TenantA).Token),
            ("analyst", tokens.CreateToken("synthetic-an", "analyst", TenantA).Token),
            ("no role", Sign("synthetic-none", [], tenant)),
            ("admin plus operator", Sign("synthetic-two", ["admin", "operator"], tenant)),
        };
        foreach (var (label, bearer) in bearers)
        {
            using var response = await Send(client, Path(Guid.NewGuid()), bearer, Body());
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, label);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "admin_required", label);
        }

        Assert.AreEqual(0, fetcher.Calls, "no fetcher access for a non-admin caller");

        // Positive control: the same request from an admin of the tenant reaches the fetcher.
        using var admin = await Send(client, Path(Guid.NewGuid()), Sign("synthetic-admin", ["admin"], tenant), Body());
        Assert.AreEqual(1, fetcher.Calls);
        Assert.AreEqual(HttpStatusCode.Created, admin.StatusCode);
    }

    [TestMethod]
    public async Task MissingTenantActorOrAnonymousIsRefusedBeforeTheFetcher()
    {
        var fetcher = new RecordingFetcher();
        using var factory = new ApiFactory(fetcher: fetcher);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-admin", "admin").Token,
                     Sign("synthetic-admin", ["admin"], ["not-a-uuid"]),
                     Sign("synthetic-admin", ["admin"], [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var response = await Send(client, Path(Guid.NewGuid()), bearer, Body());
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "tenant_required");
        }

        foreach (var name in new[] { null, "   ", "a\u0001b" })
        {
            using var response = await Send(client, Path(Guid.NewGuid()),
                Sign(name, ["admin"], [TenantA.ToString("D")]), Body());
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "actor_required");
        }

        using var anonymous = await client.PostAsJsonAsync(Path(Guid.NewGuid()), Body());
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, fetcher.Calls);
    }

    [TestMethod]
    public async Task MalformedBodiesAreRefusedBeforeTheFetcher()
    {
        var fetcher = new RecordingFetcher();
        using var factory = new ApiFactory(fetcher: fetcher);
        using var client = factory.CreateClient();
        var admin = Sign("synthetic-admin", ["admin"], [TenantA.ToString("D")]);

        var bad = new object?[]
        {
            new { requestedUri = "https://records.example.dz/a", policyVersion = "v", jurisdiction = "dz", sourceType = "t" },
            new { attemptKey = "k", policyVersion = "v", jurisdiction = "dz", sourceType = "t" },
            new { attemptKey = "k", requestedUri = "https://records.example.dz/a", jurisdiction = "dz", sourceType = "t" },
            new { attemptKey = "k", requestedUri = "https://records.example.dz/a", policyVersion = "v", sourceType = "t" },
            new { attemptKey = "k", requestedUri = "https://records.example.dz/a", policyVersion = "v", jurisdiction = "dz" },
            new { attemptKey = "  ", requestedUri = "https://records.example.dz/a", policyVersion = "v", jurisdiction = "dz", sourceType = "t" },
            new { attemptKey = new string('k', TenantFetchAttemptLimits.MaxAttemptKeyLength + 1), requestedUri = "https://records.example.dz/a", policyVersion = "v", jurisdiction = "dz", sourceType = "t" },
            new { attemptKey = "k", requestedUri = new string('u', TenantFetchAttemptLimits.MaxRequestedUriLength + 1), policyVersion = "v", jurisdiction = "dz", sourceType = "t" },
        };
        foreach (var body in bad)
        {
            using var response = await Send(client, Path(Guid.NewGuid()), admin, body);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, System.Text.Json.JsonSerializer.Serialize(body));
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_retrieval_request");
        }

        Assert.AreEqual(0, fetcher.Calls);
    }

    [TestMethod]
    public async Task TenantComesFromTheClaimAndLimitsAreServerConstants()
    {
        var fetcher = new RecordingFetcher();
        using var factory = new ApiFactory(fetcher: fetcher);
        using var client = factory.CreateClient();
        var source = Guid.NewGuid();

        // Caller-supplied tenant and limit members are ignored, never bound.
        using var response = await Send(client, Path(source),
            Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]),
            new
            {
                attemptKey = "attempt-9",
                requestedUri = "https://records.example.dz/doc/9",
                policyVersion = "2026-10.1",
                jurisdiction = "dz",
                sourceType = "court_record",
                tenantId = TenantA,
                maxResponseBytes = 1L,
                maxTotalDuration = "00:00:01"
            });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var seen = fetcher.Last!;
        Assert.AreEqual(TenantB, seen.Tenant);
        Assert.AreEqual(source, seen.Request.SourceEntryId);
        Assert.AreEqual("synthetic-admin-b", seen.Actor);
        Assert.AreEqual(TenantSourceRetrievalsController.MaxResponseBytes, seen.Request.Limits.MaxResponseBytes);
        Assert.AreEqual(TenantSourceRetrievalsController.MaxTotalDuration, seen.Request.Limits.MaxTotalDuration);
        Assert.AreEqual(new FetchExpectation("2026-10.1", "dz", "court_record"), seen.Request.Expectation);
    }

    [TestMethod]
    public async Task FetcherStatusesMapToDistinctResponses()
    {
        var due = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
        var raw = Guid.Parse("d4444444-4444-4444-8444-444444444444");
        var digest = new string('a', 64);
        var cases = new (RawSourceFetchResult Result, HttpStatusCode Status, string Needle)[]
        {
            (new(RawSourceFetchStatus.Stored, null, raw, digest, null), HttpStatusCode.Created, "\"stored\""),
            (new(RawSourceFetchStatus.Duplicate, null, raw, digest, null), HttpStatusCode.OK, "\"duplicate\""),
            (new(RawSourceFetchStatus.Refused, "host_not_allowed", null, null, null), HttpStatusCode.UnprocessableEntity, "host_not_allowed"),
            (new(RawSourceFetchStatus.RetryLater, "http_503", null, null, due), HttpStatusCode.ServiceUnavailable, "retry_later"),
            (new(RawSourceFetchStatus.SourceNotFound, "source_or_policy_not_found", null, null, null), HttpStatusCode.NotFound, "source_not_found"),
            (new(RawSourceFetchStatus.IdempotencyConflict, "attempt_key_conflict", null, null, null), HttpStatusCode.Conflict, "attempt_key_conflict"),
        };
        var admin = Sign("synthetic-admin", ["admin"], [TenantA.ToString("D")]);

        foreach (var (result, status, needle) in cases)
        {
            using var factory = new ApiFactory(fetcher: new RecordingFetcher(result));
            using var client = factory.CreateClient();
            using var response = await Send(client, Path(Guid.NewGuid()), admin, Body());
            var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(status, response.StatusCode, text);
            StringAssert.Contains(text, needle);
            StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("ev1:"), "no evidence reference");
        }
    }

    [TestMethod]
    public async Task StoredRetrievalPersistsImmutableRawRecordAndAnswersWithDigestOnly()
    {
        var cs = Database();
        if (cs is null) return;
        await ResetAsync(cs);

        var body = "<p>synthetic ruling</p>"u8.ToArray();
        using var factory = new ApiFactory(cs, handler: new StubHandler(body));
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        var source = await CreateSourceAsync(client, adminA, "https://records.example.dz/doc");
        using (var policy = await SendWith(client, HttpMethod.Put, $"/api/v1/tenant-sources/{source}/fetch-policy", adminA,
                   new
                   {
                       policyVersion = "2026-10.1", jurisdiction = "dz", sourceType = "court_record",
                       allowedHosts = new[] { "records.example.dz" }, allowedSchemes = new[] { "https" },
                       allowedPorts = new[] { 443 }, maxRedirects = 1, plaintextHttpException = false,
                       wideningApproved = true
                   }))
            Assert.AreEqual(HttpStatusCode.OK, policy.StatusCode, await policy.Content.ReadAsStringAsync());

        // Another tenant's admin cannot reach the source: same 404 as an unknown one, nothing stored.
        using (var foreign = await Send(client, Path(source), adminB, Body("foreign")))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
            StringAssert.Contains(await foreign.Content.ReadAsStringAsync(), "source_not_found");
        }
        using (var unknown = await Send(client, Path(Guid.NewGuid()), adminA, Body("unknown")))
            Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        using var stored = await Send(client, Path(source), adminA, Body("attempt-1"));
        var text = await stored.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, stored.StatusCode, text);
        var dto = System.Text.Json.JsonSerializer.Deserialize<RetrievalResponse>(
            text, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.AreEqual(RawContentDigest.Compute(body), dto.ContentSha256);
        StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("synthetic ruling|ev1:"));

        await using var db = new VeritasAtlasDbContext(Options(cs));
        Assert.AreEqual(1, await db.TenantRawRetrievals.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantRawRetrievals.AsNoTracking()
            .CountAsync(x => x.TenantId == TenantA && x.Id == dto.RawRetrievalId));

        // Same attempt key replayed: no second record, no second outbound request.
        using var replay = await Send(client, Path(source), adminA, Body("attempt-1"));
        Assert.AreEqual(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.AreEqual(1, await db.TenantRawRetrievals.AsNoTracking().CountAsync());
    }

    private sealed record SourceDto(Guid Id);

    private static async Task<Guid> CreateSourceAsync(HttpClient client, string bearer, string key)
    {
        using var response = await Send(client, "/api/v1/tenant-sources", bearer, new { canonicalKey = key });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SourceDto>())!.Id;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string path, string bearer, object? body) =>
        SendWith(client, HttpMethod.Post, path, bearer, body);

    private static async Task<HttpResponseMessage> SendWith(
        HttpClient client, HttpMethod method, string path, string bearer, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_retrievalapi";
        return builder.ConnectionString;
    }

    private static DbContextOptions<VeritasAtlasDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options;

    private static async Task ResetAsync(string connectionString)
    {
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private static string Sign(string? name, string[] roles, string[] tenants)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")) };
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
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
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)),
                SecurityAlgorithms.HmacSha256)));
    }

    private sealed record FetchCall(Guid Tenant, RawSourceFetchRequest Request, string Actor);

    private sealed class RecordingFetcher(RawSourceFetchResult? result = null) : IRawSourceFetcher
    {
        private int _calls;
        public int Calls => _calls;
        public FetchCall? Last { get; private set; }

        public Task<RawSourceFetchResult> FetchAsync(
            Guid tenantId, RawSourceFetchRequest request, string actorSubject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Last = new FetchCall(tenantId, request, actorSubject);
            return Task.FromResult(result ?? new RawSourceFetchResult(
                RawSourceFetchStatus.Stored, null, Guid.NewGuid(), new string('b', 64), null));
        }
    }

    private sealed class StubHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = request
            });
    }

    private sealed class PublicResolver : IHostAddressResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
    }

    private sealed class ApiFactory(
        string? connectionString = null, IRawSourceFetcher? fetcher = null, HttpMessageHandler? handler = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0006");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                connectionString ?? "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                if (fetcher is not null) services.AddSingleton(fetcher);
                if (handler is not null)
                    services.AddScoped<IRawSourceFetcher>(sp => new HttpRawSourceFetcher(
                        new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan },
                        new PublicResolver(),
                        sp.GetRequiredService<ITenantSourceFetchPolicyRegistry>(),
                        sp.GetRequiredService<ITenantFetchAttemptLedger>(),
                        sp.GetRequiredService<ITenantRawRetrievalLedger>(),
                        TimeProvider.System));
            });
        }
    }
}
