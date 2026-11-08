using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-10 slice 6: investigation sharing ledger. Refusals are proven before the store is touched;
/// grant, replay, revoke, re-grant, creator-only authority, anti-oracle 404s, audit, races and the
/// database guards run against real PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own
/// <c>*_shr</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationShareTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Share(Guid investigation, string grantee) =>
        $"/api/v1/tenant-investigations/{investigation}/shares/{Uri.EscapeDataString(grantee)}";

    private static string Listing(Guid investigation) => $"/api/v1/tenant-investigations/{investigation}/shares";

    private static string Token(string? subject, string role, Guid tenant) =>
        TenantInvestigationEvidenceItemTests.Sign(subject, [role], [tenant.ToString("D")]);

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string bearer) =>
        TenantInvestigationEvidenceItemTests.Send(client, method, path, bearer);

    [TestMethod]
    public async Task RefusedCallersNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store);
        using var client = factory.CreateClient();
        var inv = Guid.NewGuid();
        var a = TenantA.ToString("D");

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], ["not-a-uuid"]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["analyst"], [a]),
                     Token("s", "reviewer", TenantA),
                     Token("s", "publisher", TenantA),
                     Token("s", "admin", TenantA),
                 })
        {
            foreach (var (method, path) in new[]
                     {
                         (HttpMethod.Put, Share(inv, "bob")), (HttpMethod.Delete, Share(inv, "bob")), (HttpMethod.Get, Listing(inv)),
                     })
            {
                using var response = await Send(client, method, path, bearer);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
            }
        }

        using var anonymous = await client.PutAsync(Share(inv, "bob"), null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        foreach (var badGrantee in new[] { " bob", "bob ", new string('g', TenantInvestigationShareLimits.MaxGranteeLength + 1), "a\u0001b" })
        {
            using var response = await Send(client, HttpMethod.Put, Share(inv, badGrantee), Token("owner", "analyst", TenantA));
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, badGrantee.Length.ToString());
        }

        Assert.AreEqual(0, store.Calls, "a refused caller must never reach the store");

        // Positive control: the same shape from an author role reaches the store with claim-derived identity only.
        using var ok = await Send(
            client, HttpMethod.Put, $"{Share(inv, "bob")}?tenant={TenantB}&owner=spoofed", Token("synthetic-owner", "analyst", TenantA));
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, "synthetic-owner", "bob"), store.Granted.Single());
    }

    [TestMethod]
    public async Task GrantReplayRevokeRegrantAuditAndAntiOracleOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var ownerA = Token("synthetic-owner-a", "analyst", TenantA);
        var otherA = Token("synthetic-other-a", "operator", TenantA);
        var ownerB = Token("synthetic-owner-b", "analyst", TenantB);
        var invA = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, ownerA);
        var invB = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, ownerB);
        var shares = new EfTenantInvestigationShareStore(db, TimeProvider.System);

        Assert.IsFalse(await shares.IsAuthorizedAsync(TenantA, invA, "synthetic-other-a", CancellationToken.None));
        Assert.IsTrue(await shares.IsAuthorizedAsync(TenantA, invA, "synthetic-owner-a", CancellationToken.None));

        using var granted = await Send(client, HttpMethod.Put, Share(invA, "synthetic-other-a"), ownerA);
        Assert.AreEqual(HttpStatusCode.Created, granted.StatusCode, await granted.Content.ReadAsStringAsync());
        var first = (await granted.Content.ReadFromJsonAsync<ShareDto>())!;
        Assert.AreEqual((invA, "synthetic-other-a", "synthetic-owner-a"), (first.InvestigationId, first.GranteeSubject, first.GrantedBySubject));
        using var replay = await Send(client, HttpMethod.Put, Share(invA, "synthetic-other-a"), ownerA);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(first, await replay.Content.ReadFromJsonAsync<ShareDto>());
        Assert.IsTrue(await shares.IsAuthorizedAsync(TenantA, invA, "synthetic-other-a", CancellationToken.None));
        Assert.IsFalse(await shares.IsAuthorizedAsync(TenantB, invA, "synthetic-other-a", CancellationToken.None), "a grant never crosses tenants");
        Assert.IsFalse(await shares.IsAuthorizedAsync(TenantA, invB, "synthetic-other-a", CancellationToken.None));

        using var listed = await Send(client, HttpMethod.Get, Listing(invA), ownerA);
        Assert.AreEqual("synthetic-other-a", (await listed.Content.ReadFromJsonAsync<ShareList>())!.Items.Single().GranteeSubject);

        // Only the creator may manage sharing; the grantee, another tenant and unknown ids see the same 404.
        using var unknown = await Send(client, HttpMethod.Put, Share(Guid.NewGuid(), "x"), ownerA);
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        foreach (var (method, path, bearer) in new[]
                 {
                     (HttpMethod.Put, Share(invA, "third"), otherA), (HttpMethod.Delete, Share(invA, "synthetic-other-a"), otherA),
                     (HttpMethod.Get, Listing(invA), otherA), (HttpMethod.Put, Share(invA, "third"), ownerB),
                     (HttpMethod.Delete, Share(invA, "synthetic-other-a"), ownerB), (HttpMethod.Get, Listing(invA), ownerB),
                     (HttpMethod.Put, Share(invB, "third"), ownerA), (HttpMethod.Get, Listing(Guid.NewGuid()), ownerA),
                 })
        {
            using var response = await Send(client, method, path, bearer);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, $"{method} {path}");
            if (method == HttpMethod.Put)
            {
                Assert.AreEqual(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
            }
        }

        using var self = await Send(client, HttpMethod.Put, Share(invA, "synthetic-owner-a"), ownerA);
        Assert.AreEqual(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync(), "refused calls changed nothing");
        Assert.IsTrue(await shares.IsAuthorizedAsync(TenantA, invA, "synthetic-other-a", CancellationToken.None), "a refused revoke keeps access");

        // Revocation removes access, is idempotent, keeps the row, and a re-grant restores access.
        using var revoked = await Send(client, HttpMethod.Delete, Share(invA, "synthetic-other-a"), ownerA);
        using var revokedAgain = await Send(client, HttpMethod.Delete, Share(invA, "synthetic-other-a"), ownerA);
        using var neverShared = await Send(client, HttpMethod.Delete, Share(invA, "nobody"), ownerA);
        Assert.AreEqual(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, revokedAgain.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, neverShared.StatusCode);
        Assert.IsFalse(await shares.IsAuthorizedAsync(TenantA, invA, "synthetic-other-a", CancellationToken.None));
        using var afterRevoke = await Send(client, HttpMethod.Get, Listing(invA), ownerA);
        Assert.AreEqual(0, (await afterRevoke.Content.ReadFromJsonAsync<ShareList>())!.Items.Count);
        var kept = await db.TenantInvestigationShares.AsNoTracking().SingleAsync();
        Assert.AreEqual("synthetic-owner-a", kept.RevokedBySubject);
        Assert.IsNotNull(kept.RevokedAtUtc);

        using var regranted = await Send(client, HttpMethod.Put, Share(invA, "synthetic-other-a"), ownerA);
        Assert.AreEqual(HttpStatusCode.Created, regranted.StatusCode);
        Assert.IsTrue(await shares.IsAuthorizedAsync(TenantA, invA, "synthetic-other-a", CancellationToken.None));
        var reactivated = await db.TenantInvestigationShares.AsNoTracking().SingleAsync();
        Assert.IsNull(reactivated.RevokedAtUtc);
        Assert.IsNull(reactivated.RevokedBySubject);

        // One audit event per effective change: grant, revoke, re-grant. Replays, no-ops and refusals write none.
        var audits = await db.TenantAuditEvents.AsNoTracking()
            .Where(e => e.Action.StartsWith("investigation.share_")).ToListAsync();
        Assert.AreEqual(2, audits.Count(e => e.Action == "investigation.share_granted"));
        Assert.AreEqual(1, audits.Count(e => e.Action == "investigation.share_revoked"));
        Assert.IsTrue(audits.All(e => e.TenantId == TenantA && e.ActorSubject == "synthetic-owner-a"
                                      && e.EntityId == $"{invA:D}/synthetic-other-a"));
    }

    [TestMethod]
    public async Task ConcurrentIdenticalGrantsAndRevocationsWriteOneRowAndOneAuditEventOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var owner = Token("synthetic-owner-a", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, owner);

        var grants = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await Send(client, HttpMethod.Put, Share(inv, "synthetic-other-a"), owner);
            return response.StatusCode;
        }));
        Assert.IsTrue(grants.All(s => s is HttpStatusCode.Created or HttpStatusCode.OK), string.Join(",", grants));
        Assert.AreEqual(1, grants.Count(s => s == HttpStatusCode.Created));
        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "investigation.share_granted"));

        var revokes = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await Send(client, HttpMethod.Delete, Share(inv, "synthetic-other-a"), owner);
            return response.StatusCode;
        }));
        Assert.IsTrue(revokes.All(s => s == HttpStatusCode.NoContent));
        Assert.AreEqual(1, await db.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "investigation.share_revoked"));

        var regrants = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await Send(client, HttpMethod.Put, Share(inv, "synthetic-other-a"), owner);
            return response.StatusCode;
        }));
        Assert.IsTrue(regrants.All(s => s is HttpStatusCode.Created or HttpStatusCode.OK), string.Join(",", regrants));
        Assert.AreEqual(2, await db.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "investigation.share_granted"));
        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync(x => x.RevokedAtUtc == null));
    }

    [TestMethod]
    public async Task DatabaseRefusesForeignGrantorsCrossTenantRepointingAndDeletesOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var investigations = new EfTenantInvestigationStore(db, TimeProvider.System);
        var invA = (await investigations.CreateAsync(TenantA, new InvestigationInput("k", "T", "synthetic-owner"), CancellationToken.None)).Investigation!.Id;
        var invA2 = (await investigations.CreateAsync(TenantA, new InvestigationInput("k2", "T", "synthetic-owner"), CancellationToken.None)).Investigation!.Id;
        var store = new EfTenantInvestigationShareStore(db, TimeProvider.System);

        // Positive control: the creator can grant; every refusal below is therefore about the rule, not the setup.
        Assert.AreEqual(ShareGrantOutcome.Granted, (await store.GrantAsync(TenantA, invA, "synthetic-owner", "synthetic-bob", CancellationToken.None)).Outcome);
        Assert.AreEqual(ShareGrantOutcome.NotFound, (await store.GrantAsync(TenantA, invA, "synthetic-bob", "synthetic-carol", CancellationToken.None)).Outcome);
        Assert.AreEqual(ShareGrantOutcome.NotFound, (await store.GrantAsync(TenantB, invA, "synthetic-owner", "synthetic-bob", CancellationToken.None)).Outcome);
        Assert.AreEqual(ShareGrantOutcome.SelfGrant, (await store.GrantAsync(TenantA, invA, "synthetic-owner", "synthetic-owner", CancellationToken.None)).Outcome);
        Assert.IsNull(await store.ListActiveAsync(TenantA, invA, "synthetic-bob", CancellationToken.None));
        Assert.AreEqual(ShareRevokeOutcome.NotFound, await store.RevokeAsync(TenantA, invA, "synthetic-bob", "synthetic-bob", CancellationToken.None));

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        foreach (var (tenant, investigation, grantor) in new[]
                 {
                     (TenantA, invA, "synthetic-bob"), (TenantB, invA, "synthetic-owner"), (TenantA, Guid.NewGuid(), "synthetic-owner"),
                 })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_shares (tenant_id, investigation_id, grantee_subject, granted_by_subject, granted_at_utc) VALUES (@t, @i, 'synthetic-dave', @g, now())",
                connection);
            insert.Parameters.AddWithValue("t", tenant);
            insert.Parameters.AddWithValue("i", investigation);
            insert.Parameters.AddWithValue("g", grantor);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState);
        }

        foreach (var sql in new[]
                 {
                     "UPDATE tenant_investigation_shares SET investigation_id = '" + invA2 + "'",
                     "UPDATE tenant_investigation_shares SET grantee_subject = 'synthetic-eve'",
                     "UPDATE tenant_investigation_shares SET granted_by_subject = 'synthetic-bob'",
                     "UPDATE tenant_investigation_shares SET tenant_id = '" + TenantB + "'",
                     "DELETE FROM tenant_investigation_shares",
                     "TRUNCATE tenant_investigation_shares",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(), sql);
            Assert.AreEqual("55000", ex.SqlState, sql);
        }

        foreach (var sql in new[]
                 {
                     "UPDATE tenant_investigation_shares SET revoked_at_utc = now()",
                     "UPDATE tenant_investigation_shares SET revoked_by_subject = 'synthetic-owner'",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync(), sql);
            Assert.AreEqual("23514", ex.SqlState, sql);
        }

        await using var selfRow = new NpgsqlCommand(
            "INSERT INTO tenant_investigation_shares (tenant_id, investigation_id, grantee_subject, granted_by_subject, granted_at_utc) VALUES (@t, @i, 'synthetic-owner', 'synthetic-owner', now())",
            connection);
        selfRow.Parameters.AddWithValue("t", TenantA);
        selfRow.Parameters.AddWithValue("i", invA);
        Assert.AreEqual("23514", (await Assert.ThrowsExactlyAsync<PostgresException>(() => selfRow.ExecuteNonQueryAsync())).SqlState);

        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync());
        Assert.IsTrue((await store.ListActiveAsync(TenantA, invA, "synthetic-owner", CancellationToken.None))!.Count == 1);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GrantAsync(TenantA, invA, "synthetic-owner", " ", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GrantAsync(Guid.Empty, invA, "synthetic-owner", "synthetic-bob", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GrantAsync(
            TenantA, invA, "synthetic-owner", new string('g', TenantInvestigationShareLimits.MaxGranteeLength + 1), CancellationToken.None));

        // Store-level fail-closed parity with the controller: surrounding whitespace is refused, so
        // " synthetic-bob" can never become a grantee distinct from "synthetic-bob".
        foreach (var padded in new[] { " synthetic-bob", "synthetic-bob ", "\u00A0synthetic-bob" })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GrantAsync(TenantA, invA, "synthetic-owner", padded, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.RevokeAsync(TenantA, invA, "synthetic-owner", padded, CancellationToken.None));
        }

        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync());
        // Positive control: the trimmed form is accepted and resolves to the existing active grant.
        var ok = await store.GrantAsync(TenantA, invA, "synthetic-owner", "synthetic-bob", CancellationToken.None);
        Assert.AreEqual(ShareGrantOutcome.Replayed, ok.Outcome);
        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync());
    }

    private sealed record ShareDto(Guid InvestigationId, string GranteeSubject, string GrantedBySubject, DateTime GrantedAtUtc);

    private sealed record ShareList(List<ShareDto> Items);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_shr";
        return builder.ConnectionString;
    }

    /// <summary>Store double that counts calls and records accepted grants.</summary>
    private sealed class CountingStore : ITenantInvestigationShareStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, string Owner, string Grantee)> Granted { get; } = [];

        public Task<ShareGrantResult> GrantAsync(
            Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Granted.Add((tenantId, owner, grantee));
            return Task.FromResult(new ShareGrantResult(
                ShareGrantOutcome.Granted,
                new TenantInvestigationShareModel(investigationId, grantee, owner, DateTime.UtcNow)));
        }

        public Task<ShareRevokeOutcome> RevokeAsync(
            Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(ShareRevokeOutcome.Revoked);
        }

        public Task<IReadOnlyList<TenantInvestigationShareModel>?> ListActiveAsync(
            Guid tenantId, Guid investigationId, string owner, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantInvestigationShareModel>?>(null);
        }

        public Task<bool> IsAuthorizedAsync(
            Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(false);
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantInvestigationShareStore? store = null)
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
