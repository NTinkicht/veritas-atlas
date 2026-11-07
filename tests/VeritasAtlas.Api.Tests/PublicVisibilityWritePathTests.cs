using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-16 slice 7 (#202): the audited publisher-only write path for the public-reader facts, its restrictive reset when a
/// revision leaves the Approved/Published line, and the database trigger that refuses unaudited fact changes. The PostgreSQL
/// cases run when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_pubvis</c> database).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PublicVisibilityWritePathTests
{
    private const string SigningSecret = "publication-visibility-test-only-key-not-for-production-01";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly string Digest = new('a', 64);
    private const string Base = "/api/v1/tenant-publication-revisions";

    private static object Citation(string label = "[1]") => new
    {
        citationLabel = label, title = "Audited report", quote = "A quoted line.", sourceName = "Registry",
        asOf = "2026-01-01T00:00:00Z", effectiveDate = (string?)null, freshnessLabel = "current",
    };

    private static object Facts(long expected, string op, bool restricted = false, object[]? citations = null,
        string currency = "Current", bool superseded = false, bool freshness = true, string? expires = null) => new
    {
        expectedFactsVersion = expected, operationId = op, restricted, superseded, expiresAt = expires,
        freshnessRequired = freshness, evidenceCurrency = currency, historicalLabelPresent = false,
        citations = citations ?? [Citation()],
    };

    [TestMethod]
    public async Task OnlyAPublisherReachesTheStoreAndBadBodiesAreRejectedFirst()
    {
        var store = new RecordingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var tenant = TenantA.ToString("D");
        var id = Guid.NewGuid();

        foreach (var role in new[] { "operator", "analyst", "reviewer" })
        {
            var refused = await Put(client, Sign("synthetic-" + role, [role], [tenant]), id, Facts(0, "op-1"));
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, role);
        }

        var publisher = Sign("synthetic-publisher", ["publisher"], [tenant]);
        foreach (var bad in new object?[]
        {
            null,
            new { },
            Facts(-1, "op"),
            Facts(0, " "),
            Facts(0, new string('x', 201)),
            Facts(0, "bad\u0007id"),
            Facts(0, "op", currency: "current"),
            Facts(0, "op", currency: "Bogus"),
            Facts(0, "op", citations: new object[] { new { citationLabel = "[1]" } }),
            new { expectedFactsVersion = 0, operationId = "op", restricted = false },
        })
        {
            var response = await Put(client, publisher, id, bad);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, JsonSerializer.Serialize(bad));
        }

        Assert.AreEqual(0, store.Calls, "Refused and malformed callers must never reach the store.");
        using var anonymous = await client.PutAsJsonAsync($"{Base}/{id}/visibility", Facts(0, "op"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public void PureRulesFailClosedOnState_Policy_Citations_AndMalformedText()
    {
        PublicCitationProjection Good() => new("[1]", "T", "Q", "S", null, null, "current");
        PublicVisibilityFactsInput Facts(bool restricted, params PublicCitationProjection[] c) =>
            new(restricted, false, null, true, EvidenceCurrency.Current, false, c);

        foreach (var state in new[] { LifecycleState.Draft, LifecycleState.Submitted, LifecycleState.ChangesRequested, LifecycleState.Withdrawn })
        {
            Assert.AreEqual(VisibilityDenials.NotSettableInState,
                PublicVisibilityRules.Validate(Facts(true), true, state), state.ToString());
        }

        Assert.IsNull(PublicVisibilityRules.Validate(Facts(false, Good()), true, LifecycleState.Approved));
        Assert.IsNull(PublicVisibilityRules.Validate(Facts(true), false, LifecycleState.Published), "restricting needs no permit");
        Assert.AreEqual(VisibilityDenials.PublicDisplayNotPermitted,
            PublicVisibilityRules.Validate(Facts(false, Good()), false, LifecycleState.Approved));
        Assert.AreEqual(VisibilityDenials.CitationsRequired,
            PublicVisibilityRules.Validate(Facts(false), true, LifecycleState.Approved));
        Assert.AreEqual(VisibilityDenials.InvalidFacts,
            PublicVisibilityRules.Validate(Facts(false, Good() with { Quote = " " }), true, LifecycleState.Approved));
        Assert.AreEqual(VisibilityDenials.InvalidFacts,
            PublicVisibilityRules.Validate(Facts(false, Good() with { Title = "a\u0007b" }), true, LifecycleState.Approved));
        Assert.AreEqual(VisibilityDenials.InvalidFacts,
            PublicVisibilityRules.Validate(Facts(false, Good() with { Title = new string('t', 301) }), true, LifecycleState.Approved));
        Assert.AreEqual(VisibilityDenials.InvalidFacts,
            PublicVisibilityRules.Validate(Facts(false, Enumerable.Repeat(Good(), 51).ToArray()), true, LifecycleState.Approved));
        Assert.IsNull(PublicVisibilityRules.Validate(Facts(false, Good() with { Quote = "line1\nline2" }), true, LifecycleState.Approved));
        Assert.AreEqual(VisibilityDenials.InvalidFacts, PublicVisibilityRules.Validate(
            Facts(false, Good()) with { EvidenceCurrency = (EvidenceCurrency)99 }, true, LifecycleState.Approved));
    }

    [TestMethod]
    public async Task FactsAreSetAuditedVersionCheckedIdempotentAndResetWhenLeavingThePublicationLine()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();
        var tenant = TenantA.ToString("D");
        var author = Sign("synthetic-author", ["operator"], [tenant]);
        var reviewer = Sign("synthetic-reviewer", ["reviewer"], [tenant]);
        var publisher = Sign("synthetic-publisher", ["publisher"], [tenant]);
        var otherPublisher = Sign("synthetic-publisher-b", ["publisher"], [TenantB.ToString("D")]);

        var id = await CreateAsync(client, author, permits: true);

        // Draft/Submitted: facts cannot be set at all.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Put(client, publisher, id, Facts(0, "v-draft"))).StatusCode);
        await Step(client, author, id, "Submit", 0, "t1");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Put(client, publisher, id, Facts(0, "v-sub"))).StatusCode);
        await Step(client, reviewer, id, "Approve", 1, "t2");

        // Another tenant's publisher gets exactly the unknown-revision answer.
        var foreign = await Put(client, otherPublisher, id, Facts(0, "v-foreign"));
        var unknown = await Put(client, otherPublisher, Guid.NewGuid(), Facts(0, "v-unknown"));
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());

        // Approved: a stale facts version is a conflict, a valid write applies and bumps only the facts version.
        Assert.AreEqual(HttpStatusCode.Conflict, (await Put(client, publisher, id, Facts(5, "v-stale"))).StatusCode);
        var applied = await Put(client, publisher, id, Facts(0, "v-1"));
        Assert.AreEqual(HttpStatusCode.OK, applied.StatusCode);
        var body = await applied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(body.GetProperty("replayed").GetBoolean());
        Assert.AreEqual(1, body.GetProperty("revision").GetProperty("factsVersion").GetInt64());
        Assert.AreEqual(2, body.GetProperty("revision").GetProperty("version").GetInt64());

        // Same operation id and facts replays; same id with different facts conflicts; nothing double-applies.
        var replay = await Put(client, publisher, id, Facts(0, "v-1"));
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.IsTrue((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await Put(client, publisher, id, Facts(0, "v-1", restricted: true))).StatusCode);
        Assert.AreEqual(1L, await FactsVersionAsync(connection, id));

        // Unrestricting needs the policy permit: a revision created without it can never be unrestricted.
        var noPermit = await CreateAsync(client, author, permits: false);
        await Step(client, author, noPermit, "Submit", 0, "n1");
        await Step(client, reviewer, noPermit, "Approve", 1, "n2");
        var denied = await Put(client, publisher, noPermit, Facts(0, "v-np"));
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(VisibilityDenials.PublicDisplayNotPermitted,
            (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Publish: the stored facts make it readable by the anonymous projection with the stored citations only.
        await Step(client, publisher, id, "Publish", 2, "t3");
        using (var anonymous = await client.GetAsync($"/api/v1/public/publications/{id}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, anonymous.StatusCode);
            var view = await anonymous.Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual("[1]", view.GetProperty("citations")[0].GetProperty("citationLabel").GetString());
        }

        // A Published revision may be re-scoped; superseding it hides it immediately from the anonymous reader.
        Assert.AreEqual(HttpStatusCode.OK, (await Put(client, publisher, id, Facts(1, "v-2", superseded: true))).StatusCode);
        using (var hidden = await client.GetAsync($"/api/v1/public/publications/{id}"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        }

        Assert.AreEqual(HttpStatusCode.OK, (await Put(client, publisher, id, Facts(2, "v-3"))).StatusCode);

        // Withdraw leaves the line: facts return to the restrictive defaults with an audited Reset; no authority is inherited.
        await Step(client, publisher, id, "Withdraw", 3, "t4");
        await using var db = NewContext(connection);
        var row = await db.TenantPublicationRevisions.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.IsTrue(row.Restricted);
        Assert.IsFalse(row.Superseded);
        Assert.IsNull(row.PublicCitationsJson);
        Assert.IsNull(row.ExpiresAtUtc);
        Assert.AreEqual("Unknown", row.EvidenceCurrency);
        Assert.AreEqual(4L, row.FactsVersion);
        var audit = await db.TenantPublicationVisibilityChanges.AsNoTracking()
            .Where(x => x.RevisionId == id).OrderBy(x => x.FactsVersion).ToListAsync();
        CollectionAssert.AreEqual(new[] { "Set", "Set", "Set", "Reset" }, audit.Select(a => a.Kind).ToArray());
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4 }, audit.Select(a => a.FactsVersion).ToArray());
        Assert.AreEqual("transition:t4", audit[3].OperationId);
        Assert.IsTrue(audit.Take(3).All(a => a.ActorId == "synthetic-publisher" && a.ActorRole == "Publisher"));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Put(client, publisher, id, Facts(4, "v-after"))).StatusCode);
    }

    [TestMethod]
    public async Task TheDatabaseRefusesUnauditedFactChangesAndNonRestrictiveFactsOutsideTheLine()
    {
        var connection = Database();
        if (connection is null) return;
        await ResetAsync(connection);
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();
        var tenant = TenantA.ToString("D");
        var author = Sign("synthetic-author", ["operator"], [tenant]);
        var reviewer = Sign("synthetic-reviewer", ["reviewer"], [tenant]);
        var id = await CreateAsync(client, author, permits: true);
        await using var raw = new NpgsqlConnection(connection);
        await raw.OpenAsync();

        // A Draft cannot carry non-restrictive facts (CHECK), nor change them without any audit (trigger).
        await AssertRefused(raw, $"UPDATE tenant_publication_revisions SET restricted = false WHERE id = '{id}'");
        await Step(client, author, id, "Submit", 0, "d1");
        await Step(client, reviewer, id, "Approve", 1, "d2");

        // Approved: facts changed without a facts_version step, or with a step but no audit row, are refused at commit.
        await AssertRefused(raw, $"UPDATE tenant_publication_revisions SET restricted = false WHERE id = '{id}'");
        await AssertRefused(raw,
            $"UPDATE tenant_publication_revisions SET restricted = false, facts_version = 1 WHERE id = '{id}'");
        // Lifecycle columns may not move together with a facts-only step.
        await AssertRefused(raw,
            $"UPDATE tenant_publication_revisions SET facts_version = 1, version = version + 1 WHERE id = '{id}'");
        // The audit ledger is append-only (after one real audited write exists to protect).
        var publisher = Sign("synthetic-publisher", ["publisher"], [tenant]);
        Assert.AreEqual(HttpStatusCode.OK, (await Put(client, publisher, id, Facts(0, "db-1"))).StatusCode);
        await AssertRefused(raw, "DELETE FROM tenant_publication_visibility_changes");
        await AssertRefused(raw, "UPDATE tenant_publication_visibility_changes SET kind = 'Reset'");
        await AssertRefused(raw, "TRUNCATE tenant_publication_visibility_changes");
        // Direct fact changes still need the next facts version and a matching audit row.
        await AssertRefused(raw,
            $"UPDATE tenant_publication_revisions SET restricted = true, facts_version = 1 WHERE id = '{id}'");
        await AssertRefused(raw,
            $"UPDATE tenant_publication_revisions SET restricted = true, facts_version = 3 WHERE id = '{id}'");

        await using var db = NewContext(connection);
        var row = await db.TenantPublicationRevisions.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.IsFalse(row.Restricted);
        Assert.AreEqual(1L, row.FactsVersion);
    }

    private static async Task AssertRefused(NpgsqlConnection connection, string sql)
    {
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException)
        {
            return;
        }

        Assert.Fail("The database accepted: " + sql);
    }

    private static async Task<long> FactsVersionAsync(string connection, Guid id)
    {
        await using var db = NewContext(connection);
        return await db.TenantPublicationRevisions.AsNoTracking().Where(x => x.Id == id).Select(x => x.FactsVersion).SingleAsync();
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string bearer, bool permits)
    {
        var response = await Send(client, HttpMethod.Post, Base, bearer,
            new { contentDigest = Digest, policyPermitsPublicDisplay = permits });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task Step(HttpClient client, string bearer, Guid id, string action, long expected, string op)
    {
        var response = await Send(client, HttpMethod.Post, $"{Base}/{id}/transitions", bearer,
            new { action, expectedVersion = expected, operationId = op });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{action}: {await response.Content.ReadAsStringAsync()}");
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string bearer, Guid id, object? body) =>
        Send(client, HttpMethod.Put, $"{Base}/{id}/visibility", bearer, body);

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string bearer, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Content = body is null
            ? new StringContent(string.Empty, Encoding.UTF8, "application/json")
            : JsonContent.Create(body);
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
        builder.Database += "_pubvis";
        return builder.ConnectionString;
    }

    private static VeritasAtlasDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options);

    private static async Task ResetAsync(string connectionString)
    {
        await using var db = NewContext(connectionString);
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
            issuer: RuntimeSecurity.DefaultJwtIssuer, audience: RuntimeSecurity.DefaultJwtAudience, claims: claims,
            notBefore: now.AddMinutes(-1), expires: now.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret)), SecurityAlgorithms.HmacSha256)));
    }

    private sealed class RecordingStore : ITenantPublicationRevisionStore
    {
        private int _calls;
        public int Calls => _calls;

        public Task<TenantPublicationRevisionModel> CreateDraftAsync(
            Guid tenantId, string authorId, string contentDigest, bool policyPermitsPublicDisplay, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("The store must not be reached.");
        }

        public Task<TenantPublicationRevisionModel?> GetAsync(Guid tenantId, Guid revisionId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TenantPublicationRevisionModel?>(null);
        }

        public Task<PublicationTransitionResult> TransitionAsync(
            Guid tenantId, Guid revisionId, string actorId, LifecycleRole actorRole, LifecycleAction action,
            long expectedVersion, string operationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new PublicationTransitionResult(PublicationTransitionOutcome.NotFound, null, null));
        }

        public Task<VisibilityChangeResult> SetVisibilityAsync(
            Guid tenantId, Guid revisionId, string actorId, LifecycleRole actorRole, PublicVisibilityFactsInput facts,
            long expectedFactsVersion, string operationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new VisibilityChangeResult(VisibilityOutcome.NotFound, null, null));
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantPublicationRevisionStore? store = null)
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
                if (store is not null) services.AddSingleton(store);
            });
        }
    }
}
