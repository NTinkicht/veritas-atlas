using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// VER-P0-18 slice 2 (#298): admin dry-run retention eligibility of one raw retrieval. SYNTHETIC data only,
/// recording ledger double (the gate itself is covered by RetentionDeletionGateTests).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantRawRetrievalRetentionEndpointTests
{
    private const string SigningSecret = "retention-eligibility-test-only-key-not-for-production-0298";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static string Path(Guid id) => $"/api/v1/tenant-raw-retrievals/{id}/retention-eligibility";

    [TestMethod]
    public async Task EveryNonAdminCallerIsRefusedBeforeAnyLedger()
    {
        var ledger = new Ledgers();
        using var factory = new ApiFactory(ledger);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = new[] { TenantA.ToString("D") };

        foreach (var (label, bearer) in new (string, string)[]
                 {
                     ("operator", tokens.CreateToken("synthetic-op", "operator", TenantA).Token),
                     ("reviewer", tokens.CreateToken("synthetic-rev", "reviewer", TenantA).Token),
                     ("no role", Sign("synthetic-none", [], tenant)),
                     ("admin plus operator", Sign("synthetic-two", ["admin", "operator"], tenant)),
                 })
        {
            using var response = await Get(client, Path(Guid.NewGuid()), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, label);
        }

        Assert.AreEqual(0, ledger.Calls, "no ledger access for a non-admin caller");

        using var admin = await Get(client, Path(Guid.NewGuid()), Sign("synthetic-admin", ["admin"], tenant));
        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode, "positive control");
        Assert.IsTrue(ledger.Calls > 0);
    }

    [TestMethod]
    public async Task MissingTenantActorOrAnonymousIsRefusedBeforeAnyLedger()
    {
        var ledger = new Ledgers();
        using var factory = new ApiFactory(ledger);
        using var client = factory.CreateClient();

        foreach (var bearer in new[]
                 {
                     Sign("synthetic-admin", ["admin"], ["not-a-uuid"]),
                     Sign("synthetic-admin", ["admin"], [TenantA.ToString("D"), TenantB.ToString("D")]),
                 })
        {
            using var response = await Get(client, Path(Guid.NewGuid()), bearer);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "tenant_required");
        }

        using var noActor = await Get(client, Path(Guid.NewGuid()), Sign(null, ["admin"], [TenantA.ToString("D")]));
        Assert.AreEqual(HttpStatusCode.Forbidden, noActor.StatusCode);
        StringAssert.Contains(await noActor.Content.ReadAsStringAsync(), "actor_required");

        using var anonymous = await client.GetAsync(Path(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, ledger.Calls);
    }

    [TestMethod]
    public async Task EmptyForeignAndUnknownIdsAreIdentical404()
    {
        var known = Guid.NewGuid();
        var ledger = new Ledgers { Known = (TenantA, known) };
        using var factory = new ApiFactory(ledger);
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        using var empty = await Get(client, Path(Guid.Empty), adminA);
        using var foreign = await Get(client, Path(known), adminB);
        using var unknown = await Get(client, Path(Guid.NewGuid()), adminA);
        foreach (var r in new[] { empty, foreign, unknown })
            Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());

        using var own = await Get(client, Path(known), adminA);
        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode, "positive control: the owning tenant sees it");
    }

    [TestMethod]
    public async Task ReportsTheDenialReasonFromTheFactsAndNeverAllowsOrMutates()
    {
        var id = Guid.NewGuid();
        var ledger = new Ledgers { Known = (TenantA, id) };
        using var factory = new ApiFactory(ledger);
        using var client = factory.CreateClient();
        var admin = Sign("synthetic-admin", ["admin"], [TenantA.ToString("D")]);

        async Task<(string Outcome, string? Code, int Runs, int Atts, string Raw)> Read()
        {
            using var r = await Get(client, Path(id), admin);
            var text = await r.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode, text);
            var e = JsonDocument.Parse(text).RootElement;
            return (e.GetProperty("outcome").GetString()!, e.GetProperty("denialCode").GetString(),
                e.GetProperty("parseRunCount").GetInt32(), e.GetProperty("attestationCount").GetInt32(), text);
        }

        ledger.Runs = 0; ledger.Attestations = 0;
        var none = await Read();
        Assert.AreEqual(("Deny", "audit_trail_missing"), (none.Outcome, none.Code));

        ledger.Attestations = 2;
        var window = await Read();
        Assert.AreEqual(("Deny", "retention_not_elapsed", 0, 2), (window.Outcome, window.Code, window.Runs, window.Atts));

        ledger.Runs = 1;
        var live = await Read();
        Assert.AreEqual(("Deny", "live_reference", 1, 2), (live.Outcome, live.Code, live.Runs, live.Atts));
        StringAssert.DoesNotMatch(live.Raw, new Regex("ev1:|secret-|rawBytes|attestationKey|bindingReference"));
        Assert.AreEqual(0, ledger.Writes, "a dry run never writes");
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client.SendAsync(request);
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

    private sealed class Ledgers : ITenantRawRetrievalLedger, ITenantRetrievalAttestationLedger, ITenantParseRunLedger
    {
        private int _calls;
        public int Calls => _calls;
        public int Writes { get; private set; }
        public (Guid Tenant, Guid Id)? Known { get; init; }
        public int Runs { get; set; }
        public int Attestations { get; set; }

        public Task<TenantRawRetrievalModel?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            // With no Known entry every id resolves (used by the role/claim tests' positive controls).
            var hit = Known is null || Known == (tenantId, id);
            return Task.FromResult<TenantRawRetrievalModel?>(hit
                ? new TenantRawRetrievalModel(id, Guid.NewGuid(), "k", "https://example.test/x", new string('a', 64), 10, At, At)
                : null);
        }

        public Task<IReadOnlyList<TenantRetrievalAttestationModel>> ListForRetrievalAsync(
            Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TenantRetrievalAttestationModel>>(Enumerable.Range(0, Attestations)
                .Select(_ => new TenantRetrievalAttestationModel(
                    Guid.NewGuid(), rawRetrievalId, "secret-att-key", "r", "p", PublisherBindingMechanism.SourceRegistry,
                    "secret-binding", SourceAuthenticationResult.Authenticated, "uk", AttestedSourceType.Statute, At, At))
                .ToList());

        Task<IReadOnlyList<TenantParseRunModel>> ITenantParseRunLedger.ListForRetrievalAsync(
            Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TenantParseRunModel>>(Enumerable.Range(0, Runs)
                .Select(i => new TenantParseRunModel(
                    Guid.NewGuid(), rawRetrievalId, "run" + i, "p", "1", At, ParseRunOutcome.ParsedUnverified, null, At))
                .ToList());

        public Task<RawRetrievalRecordResult> RecordAsync(
            Guid tenantId, RawRetrievalInput input, string actorSubject, CancellationToken cancellationToken) =>
            Write<RawRetrievalRecordResult>();
        public Task<IReadOnlyList<TenantRawRetrievalModel>> ListForSourceAsync(
            Guid tenantId, Guid sourceEntryId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<RetrievalAttestationRecordResult> RecordAsync(
            Guid tenantId, RetrievalAttestationInput input, string actorSubject, CancellationToken cancellationToken) =>
            Write<RetrievalAttestationRecordResult>();
        Task<TenantRetrievalAttestationModel?> ITenantRetrievalAttestationLedger.GetAsync(
            Guid tenantId, Guid attestationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ParseRunRecordResult> RecordAsync(
            Guid tenantId, ParseRunInput input, string actorSubject, CancellationToken cancellationToken) =>
            Write<ParseRunRecordResult>();

        private Task<T> Write<T>() { Writes++; throw new NotSupportedException(); }
    }

    private sealed class ApiFactory(Ledgers ledger) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Jwt:Secret", SigningSecret);
            builder.UseSetting("Auth:Bootstrap:Username", "synthetic-bootstrap-operator");
            builder.UseSetting("Auth:Bootstrap:Password", "synthetic-bootstrap-password-not-real-0298");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAccessTokenRevocationStore>(new InMemoryAccessTokenRevocationStore());
                services.AddSingleton<ITenantRawRetrievalLedger>(ledger);
                services.AddSingleton<ITenantRetrievalAttestationLedger>(ledger);
                services.AddSingleton<ITenantParseRunLedger>(ledger);
            });
        }
    }
}
