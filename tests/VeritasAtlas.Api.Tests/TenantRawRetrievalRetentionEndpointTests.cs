using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    private static string PutPath(Guid id) => $"/api/v1/tenant-raw-retrievals/{id}/retention";

    private static Task<HttpResponseMessage> Put(HttpClient client, string path, string bearer, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client.SendAsync(request);
    }

    [TestMethod]
    public async Task RecordingRetentionRefusesEveryNonAdminCallerBeforeTheStore()
    {
        var ledger = new Ledgers();
        using var factory = new ApiFactory(ledger);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<JwtTokenService>();
        var tenant = new[] { TenantA.ToString("D") };
        var body = new { legalHold = true };

        foreach (var bearer in new[]
                 {
                     tokens.CreateToken("synthetic-op", "operator", TenantA).Token,
                     tokens.CreateToken("synthetic-pub", "publisher", TenantA).Token,
                     Sign("synthetic-none", [], tenant),
                     Sign("synthetic-two", ["admin", "analyst"], tenant),
                     Sign("synthetic-admin", ["admin"], ["not-a-uuid"]),
                     Sign(null, ["admin"], tenant),
                 })
        {
            using var response = await Put(client, PutPath(Guid.NewGuid()), bearer, body);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var anonymous = await client.PutAsJsonAsync(PutPath(Guid.NewGuid()), body);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, ledger.Calls, "no ledger access for a refused caller");
        Assert.AreEqual(0, ledger.Appends.Count);

        using var admin = await Put(client, PutPath(Guid.NewGuid()), Sign("synthetic-admin", ["admin"], tenant), body);
        Assert.AreEqual(HttpStatusCode.OK, admin.StatusCode, "positive control");
        Assert.AreEqual(1, ledger.Appends.Count);
        Assert.AreEqual(("synthetic-admin", true), (ledger.Appends[0].Actor, ledger.Appends[0].Hold));
    }

    [TestMethod]
    public async Task RecordingRetentionRejectsPastWindowsUnknownIdsAndMapsStoreConflicts()
    {
        var known = Guid.NewGuid();
        var ledger = new Ledgers { Known = (TenantA, known) };
        using var factory = new ApiFactory(ledger);
        using var client = factory.CreateClient();
        var adminA = Sign("synthetic-admin-a", ["admin"], [TenantA.ToString("D")]);
        var adminB = Sign("synthetic-admin-b", ["admin"], [TenantB.ToString("D")]);

        using var past = await Put(client, PutPath(known), adminA, new { legalHold = false, retainUntilUtc = "2020-01-01T00:00:00Z" });
        Assert.AreEqual(HttpStatusCode.BadRequest, past.StatusCode);
        using var missingBody = await Put(client, PutPath(known), adminA, null);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingBody.StatusCode);
        Assert.AreEqual(0, ledger.Appends.Count);

        using var foreign = await Put(client, PutPath(known), adminB, new { legalHold = true });
        using var unknown = await Put(client, PutPath(Guid.NewGuid()), adminA, new { legalHold = true });
        using var empty = await Put(client, PutPath(Guid.Empty), adminA, new { legalHold = true });
        foreach (var r in new[] { foreign, unknown, empty }) Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        Assert.AreEqual(0, ledger.Appends.Count, "a foreign or unknown retrieval is never recorded");

        var future = DateTime.UtcNow.AddDays(30);
        using var ok = await Put(client, PutPath(known), adminA, new { legalHold = false, retainUntilUtc = future });
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, "positive control");
        Assert.AreEqual(1, ledger.Appends.Count);

        ledger.AppendOutcome = RetentionRecordAppendOutcome.WindowShortened;
        using var shortened = await Put(client, PutPath(known), adminA, new { legalHold = false, retainUntilUtc = future });
        Assert.AreEqual(HttpStatusCode.Conflict, shortened.StatusCode);
        StringAssert.Contains(await shortened.Content.ReadAsStringAsync(), "retention_window_shortened");
    }

    [TestMethod]
    public async Task EligibilityUsesTheLatestRecordedHoldAndWindowAndAllowsOnlyWhenAllGatesPass()
    {
        var id = Guid.NewGuid();
        var ledger = new Ledgers { Known = (TenantA, id), Attestations = 1, Runs = 0 };
        using var factory = new ApiFactory(ledger);
        using var client = factory.CreateClient();
        var admin = Sign("synthetic-admin", ["admin"], [TenantA.ToString("D")]);

        async Task<JsonElement> Read()
        {
            using var r = await Get(client, Path(id), admin);
            return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        var none = await Read();
        Assert.AreEqual("retention_not_elapsed", none.GetProperty("denialCode").GetString());
        Assert.AreEqual(0, none.GetProperty("recordVersion").GetInt32());

        var past = DateTime.UtcNow.AddDays(-1);
        ledger.Record = new TenantRetentionRecordModel(2, true, past, "synthetic-admin", At);
        var held = await Read();
        Assert.AreEqual(("Deny", "legal_hold", 2, true),
            (held.GetProperty("outcome").GetString(), held.GetProperty("denialCode").GetString(),
                held.GetProperty("recordVersion").GetInt32(), held.GetProperty("legalHold").GetBoolean()),
            "a legal hold denies even after the window elapsed");

        ledger.Record = new TenantRetentionRecordModel(3, false, DateTime.UtcNow.AddDays(30), "synthetic-admin", At);
        Assert.AreEqual("retention_not_elapsed", (await Read()).GetProperty("denialCode").GetString());

        ledger.Record = new TenantRetentionRecordModel(4, false, past, "synthetic-admin", At);
        var allowed = await Read();
        Assert.AreEqual("Allow", allowed.GetProperty("outcome").GetString(), "positive control: all gates pass");
        Assert.AreEqual(JsonValueKind.Null, allowed.GetProperty("denialCode").ValueKind);

        ledger.Runs = 1;
        Assert.AreEqual("live_reference", (await Read()).GetProperty("denialCode").GetString());
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

    private sealed class Ledgers : ITenantRawRetrievalLedger, ITenantRetrievalAttestationLedger, ITenantParseRunLedger,
        ITenantRetentionRecordStore
    {
        public TenantRetentionRecordModel? Record { get; set; }
        public RetentionRecordAppendOutcome AppendOutcome { get; set; } = RetentionRecordAppendOutcome.Appended;
        public List<(bool Hold, DateTime? Until, string Actor)> Appends { get; } = [];

        public Task<TenantRetentionRecordModel?> GetLatestAsync(Guid tenantId, Guid id, CancellationToken ct) =>
            Task.FromResult(Record);

        public Task<RetentionRecordAppendResult> AppendAsync(
            Guid tenantId, Guid id, bool legalHold, DateTime? until, string actor, CancellationToken ct)
        {
            Appends.Add((legalHold, until, actor));
            return Task.FromResult(new RetentionRecordAppendResult(AppendOutcome,
                AppendOutcome is RetentionRecordAppendOutcome.Appended or RetentionRecordAppendOutcome.NoChange
                    ? new TenantRetentionRecordModel(1, legalHold, until, actor, At) : null));
        }

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
                services.AddSingleton<ITenantRetentionRecordStore>(ledger);
            });
        }
    }
}
