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
/// VER-P1-01 slice 3: private per-subject investigation folder. Refusals and name validation are
/// proven before the store is touched; set, replay, change, clear, audit, privacy, closed-investigation
/// refusal, tenant isolation and database guards run against real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_folders</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationFolderTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Folder(Guid investigation) => $"/api/v1/tenant-investigations/{investigation}/folder";

    private static string Token(string? subject, string role, Guid tenant) =>
        TenantInvestigationEvidenceItemTests.Sign(subject, [role], [tenant.ToString("D")]);

    private static Task<HttpResponseMessage> Send(HttpClient c, HttpMethod m, string path, string bearer, object? body = null) =>
        TenantInvestigationEvidenceItemTests.Send(c, m, path, bearer, body);

    private static object Name(string name) => new { name };

    [TestMethod]
    public async Task RefusedCallersNeverReachTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store, shares: new FixedShares(true));
        using var client = factory.CreateClient();
        var a = TenantA.ToString("D");
        var inv = Guid.NewGuid();

        foreach (var bearer in new[]
                 {
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], []),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], ["not-a-uuid"]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", [], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "operator"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("a\u0001b", ["analyst"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["intruder"], [a]),
                 })
        {
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Get })
            {
                using var response = await Send(client, method, Folder(inv), bearer, method == HttpMethod.Put ? Name("ok") : null);
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, method.Method);
            }
        }

        foreach (var role in new[] { "reviewer", "publisher", "admin" })
        {
            using var put = await Send(client, HttpMethod.Put, Folder(inv), Token("s", role, TenantA), Name("ok"));
            using var delete = await Send(client, HttpMethod.Delete, Folder(inv), Token("s", role, TenantA));
            Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode, role);
            Assert.AreEqual(HttpStatusCode.Forbidden, delete.StatusCode, role);
        }

        using var anonymous = await client.GetAsync(Folder(inv));
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, store.Calls, "a refused caller must never reach the store");

        // Positive control: tenant and owner come from the token, never from the request.
        using var ok = await Send(client, HttpMethod.Put, $"{Folder(inv)}?tenant={TenantB}&subject=spoofed",
            Token("synthetic-analyst", "analyst", TenantA), Name("Case files"));
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual((TenantA, "synthetic-analyst"), store.Set.Single());
        using var reviewerGet = await Send(client, HttpMethod.Get, Folder(inv), Token("synthetic-reviewer", "reviewer", TenantA));
        Assert.AreEqual(HttpStatusCode.OK, reviewerGet.StatusCode);
    }

    [TestMethod]
    public async Task InvalidNamesAreRefusedBeforeTheStore()
    {
        var store = new CountingStore();
        using var factory = new ApiFactory(store: store, shares: new FixedShares(true));
        using var client = factory.CreateClient();
        var bearer = Token("synthetic-analyst", "analyst", TenantA);
        var inv = Guid.NewGuid();

        foreach (var bad in new[]
                 {
                     "", "   ", "a  b", "semi;colon", "caf\u00e9", "ctl\u0001", "tab\tx", "a/b", "<b>", new string('f', 61)
                 })
        {
            using var put = await Send(client, HttpMethod.Put, Folder(inv), bearer, Name(bad));
            Assert.AreEqual(HttpStatusCode.BadRequest, put.StatusCode, bad);
            StringAssert.Contains(await put.Content.ReadAsStringAsync(), "invalid_folder_name");
        }

        using var missing = await Send(client, HttpMethod.Put, Folder(inv), bearer, new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.AreEqual(0, store.Calls, "an invalid name must never reach the store");

        // Positive control: boundary length and trimmed names pass.
        using var longest = await Send(client, HttpMethod.Put, Folder(inv), bearer, Name(new string('f', 60)));
        Assert.AreEqual(HttpStatusCode.Created, longest.StatusCode);
        using var trimmed = await Send(client, HttpMethod.Put, Folder(inv), bearer, Name("  Q4 review_1.a-b  "));
        Assert.AreEqual(HttpStatusCode.Created, trimmed.StatusCode);
        Assert.AreEqual("Q4 review_1.a-b", store.LastName);
    }

    [TestMethod]
    public async Task UnauthorizedInvestigationCallersNeverReachTheFolderStore()
    {
        var store = new CountingStore();
        using var denied = new ApiFactory(store: store, shares: new FixedShares(false));
        using var client = denied.CreateClient();
        var inv = Guid.NewGuid();
        var analyst = Token("synthetic-analyst", "analyst", TenantA);

        using var put = await Send(client, HttpMethod.Put, Folder(inv), analyst, Name("ok"));
        using var delete = await Send(client, HttpMethod.Delete, Folder(inv), analyst);
        using var get = await Send(client, HttpMethod.Get, Folder(inv), analyst);
        Assert.AreEqual(HttpStatusCode.NotFound, put.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, get.StatusCode);
        Assert.AreEqual(0, store.Calls);

        using var allowed = new ApiFactory(store: store, shares: new FixedShares(true));
        using var allowedClient = allowed.CreateClient();
        using var ok = await Send(allowedClient, HttpMethod.Put, Folder(inv), analyst, Name("ok"));
        Assert.AreEqual(HttpStatusCode.Created, ok.StatusCode);
        Assert.AreEqual(1, store.Calls);
    }

    [TestMethod]
    public async Task FoldersArePrivateAuditedAndTenantIsolatedOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var bob = Token("synthetic-bob", "operator", TenantA);
        var aliceB = Token("synthetic-alice", "analyst", TenantB);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, alice);

        using var unset = await Send(client, HttpMethod.Get, Folder(inv), alice);
        Assert.AreEqual(HttpStatusCode.NotFound, unset.StatusCode);
        StringAssert.Contains(await unset.Content.ReadAsStringAsync(), "investigation_folder_not_set");

        using var created = await Send(client, HttpMethod.Put, Folder(inv), alice, Name("Inbox"));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var first = (await created.Content.ReadFromJsonAsync<FolderDto>())!;
        Assert.AreEqual("Inbox", first.Name);
        using var replay = await Send(client, HttpMethod.Put, Folder(inv), alice, Name("Inbox"));
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(first, await replay.Content.ReadFromJsonAsync<FolderDto>());
        using var changed = await Send(client, HttpMethod.Put, Folder(inv), alice, Name("Archive"));
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        Assert.AreEqual("Archive", await GetName(client, inv, alice));

        // A same-tenant caller without a grant and a foreign tenant see what an unknown id answers.
        using var unknown = await Send(client, HttpMethod.Put, Folder(Guid.NewGuid()), bob, Name("x"));
        using var ungranted = await Send(client, HttpMethod.Put, Folder(inv), bob, Name("x"));
        using var ungrantedGet = await Send(client, HttpMethod.Get, Folder(inv), bob);
        using var foreign = await Send(client, HttpMethod.Put, Folder(inv), aliceB, Name("x"));
        Assert.AreEqual(HttpStatusCode.NotFound, ungranted.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, ungrantedGet.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.AreEqual(unknownBody, await ungranted.Content.ReadAsStringAsync());
        Assert.AreEqual(unknownBody, await foreign.Content.ReadAsStringAsync());

        // After a grant, the grantee's folder is independent: neither sees the other's.
        using var grant = await Send(client, HttpMethod.Put, $"/api/v1/tenant-investigations/{inv}/shares/synthetic-bob", alice);
        Assert.AreEqual(HttpStatusCode.Created, grant.StatusCode);
        using var bobUnset = await Send(client, HttpMethod.Get, Folder(inv), bob);
        StringAssert.Contains(await bobUnset.Content.ReadAsStringAsync(), "investigation_folder_not_set");
        using var bobSet = await Send(client, HttpMethod.Put, Folder(inv), bob, Name("Bob stuff"));
        Assert.AreEqual(HttpStatusCode.Created, bobSet.StatusCode);
        Assert.AreEqual("Bob stuff", await GetName(client, inv, bob));
        Assert.AreEqual("Archive", await GetName(client, inv, alice));
        using var bobClears = await Send(client, HttpMethod.Delete, Folder(inv), bob);
        Assert.AreEqual(HttpStatusCode.NoContent, bobClears.StatusCode);
        Assert.AreEqual("Archive", await GetName(client, inv, alice), "clearing never touches another subject's folder");
        using var bobClearsAgain = await Send(client, HttpMethod.Delete, Folder(inv), bob);
        Assert.AreEqual(HttpStatusCode.NoContent, bobClearsAgain.StatusCode, "clearing an unset folder is a no-op");

        // One audit event per effective change (no replay, no no-op clear), attributed to the owner.
        var actions = await db.TenantAuditEvents.AsNoTracking()
            .Where(x => x.TenantId == TenantA && x.EntityType == EfTenantInvestigationFolderStore.AuditEntityType)
            .Select(x => x.Action + "|" + x.ActorSubject + "|" + x.EntityId).ToListAsync();
        var id = inv.ToString("D");
        CollectionAssert.AreEquivalent(
            new[]
            {
                $"investigation.folder_set|synthetic-alice|{id}", $"investigation.folder_set|synthetic-alice|{id}",
                $"investigation.folder_set|synthetic-bob|{id}", $"investigation.folder_cleared|synthetic-bob|{id}"
            },
            actions);
        Assert.AreEqual(0, await db.TenantInvestigationFolders.AsNoTracking().CountAsync(x => x.TenantId == TenantB));
    }

    [TestMethod]
    public async Task ClosedInvestigationsRefuseFolderChangesAndReopenRestoresThemOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        var alice = Token("synthetic-alice", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, alice);
        using var kept = await Send(client, HttpMethod.Put, Folder(inv), alice, Name("kept"));
        Assert.AreEqual(HttpStatusCode.Created, kept.StatusCode);

        using var close = await Send(client, HttpMethod.Post, $"/api/v1/tenant-investigations/{inv}/close", alice, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.OK, close.StatusCode);
        using var setClosed = await Send(client, HttpMethod.Put, Folder(inv), alice, Name("late"));
        using var clearClosed = await Send(client, HttpMethod.Delete, Folder(inv), alice);
        Assert.AreEqual(HttpStatusCode.Conflict, setClosed.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, clearClosed.StatusCode);
        StringAssert.Contains(await setClosed.Content.ReadAsStringAsync(), "investigation_closed");
        Assert.AreEqual("kept", await GetName(client, inv, alice), "reads still work while closed");

        // The database refuses the same writes even when the application check is bypassed.
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        foreach (var sql in new[]
                 {
                     "UPDATE tenant_investigation_folders SET name = 'raw' WHERE tenant_id = @t",
                     "DELETE FROM tenant_investigation_folders WHERE tenant_id = @t"
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("t", TenantA);
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.AreEqual("the investigation is closed", error.MessageText);
        }

        using var reopen = await Send(client, HttpMethod.Post, $"/api/v1/tenant-investigations/{inv}/reopen", alice, new { expectedVersion = 1 });
        Assert.AreEqual(HttpStatusCode.OK, reopen.StatusCode);
        using var setOpen = await Send(client, HttpMethod.Put, Folder(inv), alice, Name("late"));
        Assert.AreEqual(HttpStatusCode.OK, setOpen.StatusCode);
        using var clearOpen = await Send(client, HttpMethod.Delete, Folder(inv), alice);
        Assert.AreEqual(HttpStatusCode.NoContent, clearOpen.StatusCode);
    }

    [TestMethod]
    public async Task DatabaseRefusesCrossTenantRepointedAndMalformedFoldersOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var investigations = new EfTenantInvestigationStore(db, TimeProvider.System);
        var invA = (await investigations.CreateAsync(TenantA, new InvestigationInput("k", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var invA2 = (await investigations.CreateAsync(TenantA, new InvestigationInput("k2", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var store = new EfTenantInvestigationFolderStore(db, TimeProvider.System);
        Assert.AreEqual(FolderSetOutcome.Created, (await store.SetAsync(TenantA, invA, "ok", "synthetic", CancellationToken.None)).Outcome);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        // Wrong tenant for the investigation, unknown investigation: guard trigger (23514).
        foreach (var (tenant, investigation) in new[] { (TenantB, invA), (TenantA, Guid.NewGuid()) })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_folders (tenant_id, investigation_id, subject, name, updated_at_utc) VALUES (@t, @i, 'other', 'x', now())",
                connection);
            insert.Parameters.AddWithValue("t", tenant);
            insert.Parameters.AddWithValue("i", investigation);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState);
        }

        // Malformed names and subjects are refused by check constraints (positive control above: 'ok').
        foreach (var (subject, name) in new[] { ("other", "a  b"), ("other", " lead"), ("other", ""), ("other", "x;y"), (" ", "x"), ("a\u0001b", "x") })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_folders (tenant_id, investigation_id, subject, name, updated_at_utc) VALUES (@t, @i, @s, @n, now())",
                connection);
            insert.Parameters.AddWithValue("t", TenantA);
            insert.Parameters.AddWithValue("i", invA);
            insert.Parameters.AddWithValue("s", subject);
            insert.Parameters.AddWithValue("n", name);
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.AreEqual("23514", ex.SqlState, $"{subject}/{name}");
        }

        // The key cannot be re-pointed; the name can change.
        await using var repoint = new NpgsqlCommand("UPDATE tenant_investigation_folders SET investigation_id = @i", connection);
        repoint.Parameters.AddWithValue("i", invA2);
        var repointError = await Assert.ThrowsExactlyAsync<PostgresException>(() => repoint.ExecuteNonQueryAsync());
        Assert.AreEqual("55000", repointError.SqlState);
        await using var rename = new NpgsqlCommand("UPDATE tenant_investigation_folders SET name = 'renamed'", connection);
        Assert.AreEqual(1, await rename.ExecuteNonQueryAsync());

        Assert.AreEqual(1, await db.TenantInvestigationFolders.AsNoTracking().CountAsync());
        Assert.AreEqual(FolderSetOutcome.NotFound, (await store.SetAsync(TenantB, invA, "ok", "synthetic", CancellationToken.None)).Outcome);
        Assert.IsFalse((await store.GetAsync(TenantB, invA, "synthetic", CancellationToken.None)).InvestigationFound);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SetAsync(TenantA, invA, " bad ", "synthetic", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SetAsync(TenantA, invA, "ok", " ", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SetAsync(Guid.Empty, invA, "ok", "synthetic", CancellationToken.None));
    }

    [TestMethod]
    public async Task ClearWaitsForTheSubjectFolderLockSoItCannotRaceASetOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var investigations = new EfTenantInvestigationStore(db, TimeProvider.System);
        var inv = (await investigations.CreateAsync(TenantA, new InvestigationInput("k", "T", "synthetic"), CancellationToken.None)).Investigation!.Id;
        var store = new EfTenantInvestigationFolderStore(db, TimeProvider.System);
        Assert.AreEqual(FolderSetOutcome.Created, (await store.SetAsync(TenantA, inv, "a", "synthetic", CancellationToken.None)).Outcome);

        // Hold the lock a concurrent SetAsync would hold; ClearAsync must wait for it.
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var holder = await connection.BeginTransactionAsync();
        var lockKey = $"investigation_folder:{TenantA:D}:{inv:D}:synthetic";
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@k, 0))", connection, holder))
        {
            take.Parameters.AddWithValue("k", lockKey);
            await take.ExecuteNonQueryAsync();
        }

        await using var db2 = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var clear = new EfTenantInvestigationFolderStore(db2, TimeProvider.System)
            .ClearAsync(TenantA, inv, "synthetic", CancellationToken.None);
        await Task.Delay(1500);
        Assert.IsFalse(clear.IsCompleted, "clear must wait for the per-subject folder lock");
        await holder.CommitAsync();
        Assert.AreEqual(FolderClearOutcome.Cleared, await clear);
    }

    private static async Task<string> GetName(HttpClient client, Guid investigation, string bearer)
    {
        using var response = await Send(client, HttpMethod.Get, Folder(investigation), bearer);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!.Name;
    }

    private sealed record FolderDto(Guid InvestigationId, string Name, DateTime UpdatedAtUtc);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_folders";
        return builder.ConnectionString;
    }

    /// <summary>Store double that counts calls and records the accepted caller and name.</summary>
    private sealed class CountingStore : ITenantInvestigationFolderStore
    {
        private int _calls;
        public int Calls => _calls;
        public List<(Guid Tenant, string Subject)> Set { get; } = [];
        public string? LastName { get; private set; }

        public Task<FolderSetResult> SetAsync(
            Guid tenantId, Guid investigationId, string name, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Set.Add((tenantId, subject));
            LastName = name;
            return Task.FromResult(new FolderSetResult(
                FolderSetOutcome.Created, new TenantInvestigationFolderModel(investigationId, name, subject, DateTime.UtcNow)));
        }

        public Task<FolderClearOutcome> ClearAsync(
            Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(FolderClearOutcome.Cleared);
        }

        public Task<FolderGetResult> GetAsync(
            Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new FolderGetResult(
                true, new TenantInvestigationFolderModel(investigationId, "stub", subject, DateTime.UtcNow)));
        }
    }

    /// <summary>Share store double answering every authorization question the same way.</summary>
    private sealed class FixedShares(bool authorized) : ITenantInvestigationShareStore
    {
        public Task<bool> IsAuthorizedAsync(Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken) =>
            Task.FromResult(authorized);

        public Task<ShareGrantResult> GrantAsync(Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareRevokeOutcome> RevokeAsync(Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantInvestigationShareModel>?> ListActiveAsync(Guid tenantId, Guid investigationId, string owner, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> ListActiveGrantedInvestigationIdsAsync(Guid tenantId, string subject, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ApiFactory(
        string? connectionString = null, ITenantInvestigationFolderStore? store = null, ITenantInvestigationShareStore? shares = null)
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
                if (shares is not null) services.AddSingleton(shares);
            });
        }
    }
}
