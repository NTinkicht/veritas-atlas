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
/// VER-P0-10 slice 8: close and reopen an investigation through append-only state events.
/// Refusals are proven before the store is touched; the round trip, optimistic version, replay,
/// closed-write refusals (each with a positive control: the same request succeeds when open),
/// authorization, races and the database guards run against real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_state</c> database). Fixtures are SYNTHETIC.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantInvestigationStateTests
{
    private const string SigningSecret = "evidence-items-test-only-key-not-for-production-001";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static string Close(Guid id) => $"/api/v1/tenant-investigations/{id}/close";
    private static string Reopen(Guid id) => $"/api/v1/tenant-investigations/{id}/reopen";
    private static string Detail(Guid id) => $"/api/v1/tenant-investigations/{id}";
    private static string Items(Guid id) => $"/api/v1/tenant-investigations/{id}/evidence-items";
    private static string Notes(Guid id, Guid item) => $"{Items(id)}/{item}/annotations";
    private static string Mark(Guid id, Guid item) => $"{Items(id)}/{item}/bookmark";
    private static string Share(Guid id, string grantee) => $"{Detail(id)}/shares/{grantee}";

    private static string Token(string subject, string role, Guid tenant) =>
        TenantInvestigationEvidenceItemTests.Sign(subject, [role], [tenant.ToString("D")]);

    private static Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string bearer, object? body = null) =>
        TenantInvestigationEvidenceItemTests.Send(client, method, path, bearer, body);

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
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst"], [a, TenantB.ToString("D")]),
                     TenantInvestigationEvidenceItemTests.Sign("s", ["analyst", "reviewer"], [a]),
                     TenantInvestigationEvidenceItemTests.Sign(null, ["analyst"], [a]),
                     Token("s", "reviewer", TenantA), Token("s", "publisher", TenantA), Token("s", "admin", TenantA),
                 })
        {
            foreach (var path in new[] { Close(inv), Reopen(inv) })
            {
                using var response = await Send(client, HttpMethod.Post, path, bearer, new { expectedVersion = 0 });
                Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, path);
            }
        }

        foreach (var body in new object?[] { null, new { }, new { expectedVersion = -1 }, new { expectedVersion = "x" } })
        {
            using var response = await Send(client, HttpMethod.Post, Close(inv), Token("s", "analyst", TenantA), body ?? new { });
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var anonymous = await client.PostAsJsonAsync(Close(inv), new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(0, store.Calls);
    }

    [TestMethod]
    public async Task CloseReopenRoundTripVersionReplayAndClosedWritesOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var v1 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var v2 = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "b");
        var owner = Token("synthetic-owner", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, owner);

        var fresh = await GetAsync(client, inv, owner);
        Assert.AreEqual(("open", 0), (fresh.State, fresh.Version));

        // Positive controls: while open every write below is accepted.
        using var collected = await Send(client, HttpMethod.Post, Items(inv), owner, new { evidenceVerificationId = v1 });
        Assert.AreEqual(HttpStatusCode.Created, collected.StatusCode);
        var item = (await collected.Content.ReadFromJsonAsync<ItemDto>())!.Id;
        using var noteOpen = await Send(client, HttpMethod.Post, Notes(inv, item), owner, new { text = "first note" });
        Assert.AreEqual(HttpStatusCode.Created, noteOpen.StatusCode);
        using var markOpen = await Send(client, HttpMethod.Put, Mark(inv, item), owner);
        Assert.AreEqual(HttpStatusCode.Created, markOpen.StatusCode);
        using var shareOpen = await Send(client, HttpMethod.Put, Share(inv, "synthetic-bob"), owner);
        Assert.AreEqual(HttpStatusCode.Created, shareOpen.StatusCode);

        // Stale version, then close.
        using var stale = await Send(client, HttpMethod.Post, Close(inv), owner, new { expectedVersion = 5 });
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual("version_conflict", (await stale.Content.ReadFromJsonAsync<ErrorDto>())!.Code);
        using var closed = await Send(client, HttpMethod.Post, Close(inv), owner, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.OK, closed.StatusCode);
        var afterClose = (await closed.Content.ReadFromJsonAsync<InvestigationDto>())!;
        Assert.AreEqual(("closed", 1), (afterClose.State, afterClose.Version));

        // Replay of the same request is idempotent: no second event, same answer.
        using var replay = await Send(client, HttpMethod.Post, Close(inv), owner, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(afterClose, await replay.Content.ReadFromJsonAsync<InvestigationDto>());
        using var noChange = await Send(client, HttpMethod.Post, Close(inv), owner, new { expectedVersion = 1 });
        Assert.AreEqual(HttpStatusCode.Conflict, noChange.StatusCode);
        Assert.AreEqual("state_unchanged", (await noChange.Content.ReadFromJsonAsync<ErrorDto>())!.Code);
        Assert.AreEqual(1, await db.TenantInvestigationStateEvents.AsNoTracking().CountAsync());

        // Detail and list expose state and version; the creation record is untouched.
        Assert.AreEqual(afterClose, await GetAsync(client, inv, owner));
        using var list = await Send(client, HttpMethod.Get, "/api/v1/tenant-investigations", owner);
        var listed = (await list.Content.ReadFromJsonAsync<ListDto>())!.Items.Single();
        Assert.AreEqual(("closed", 1), (listed.State, listed.Version));

        // Closed: every write is refused with 409 investigation_closed; reads still work.
        var refused = new[]
        {
            await Send(client, HttpMethod.Post, Items(inv), owner, new { evidenceVerificationId = v2 }),
            await Send(client, HttpMethod.Post, Items(inv), owner, new { evidenceVerificationId = v1 }),
            await Send(client, HttpMethod.Post, Notes(inv, item), owner, new { text = "second note" }),
            await Send(client, HttpMethod.Post, Notes(inv, item), owner, new { text = "first note" }),
            await Send(client, HttpMethod.Put, Mark(inv, item), Token("synthetic-bob", "operator", TenantA)),
            await Send(client, HttpMethod.Delete, Mark(inv, item), owner),
            await Send(client, HttpMethod.Put, Share(inv, "synthetic-carol"), owner),
            await Send(client, HttpMethod.Delete, Share(inv, "synthetic-bob"), owner),
        };
        foreach (var response in refused)
        {
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, response.RequestMessage!.RequestUri!.AbsolutePath);
            Assert.AreEqual("investigation_closed", (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Code);
            response.Dispose();
        }

        Assert.AreEqual(1, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantInvestigationAnnotations.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantInvestigationBookmarks.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync(s => s.RevokedAtUtc == null));
        using var read = await Send(client, HttpMethod.Get, Items(inv), owner);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);

        // Reopen: the very requests refused above now succeed.
        using var reopenStale = await Send(client, HttpMethod.Post, Reopen(inv), owner, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.Conflict, reopenStale.StatusCode);
        using var reopened = await Send(client, HttpMethod.Post, Reopen(inv), owner, new { expectedVersion = 1 });
        var afterReopen = (await reopened.Content.ReadFromJsonAsync<InvestigationDto>())!;
        Assert.AreEqual(("open", 2), (afterReopen.State, afterReopen.Version));
        using var collectedAgain = await Send(client, HttpMethod.Post, Items(inv), owner, new { evidenceVerificationId = v2 });
        Assert.AreEqual(HttpStatusCode.Created, collectedAgain.StatusCode);
        using var noteAgain = await Send(client, HttpMethod.Post, Notes(inv, item), owner, new { text = "second note" });
        Assert.AreEqual(HttpStatusCode.Created, noteAgain.StatusCode);
        using var unmark = await Send(client, HttpMethod.Delete, Mark(inv, item), owner);
        Assert.AreEqual(HttpStatusCode.NoContent, unmark.StatusCode);
        using var revoke = await Send(client, HttpMethod.Delete, Share(inv, "synthetic-bob"), owner);
        Assert.AreEqual(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.AreEqual(0, await db.TenantInvestigationBookmarks.AsNoTracking().CountAsync());

        // Exactly one audit event per effective transition; replay, stale and no-change write none.
        var audits = await db.TenantAuditEvents.AsNoTracking()
            .Where(e => e.Action == "investigation.closed" || e.Action == "investigation.reopened").ToListAsync();
        Assert.AreEqual(1, audits.Count(e => e.Action == "investigation.closed"));
        Assert.AreEqual(1, audits.Count(e => e.Action == "investigation.reopened"));
        Assert.IsTrue(audits.All(e => e.TenantId == TenantA && e.EntityId == inv.ToString("D")
                                      && e.ActorSubject == "synthetic-owner"));
    }

    [TestMethod]
    public async Task OnlyCreatorOrActiveGranteeMayTransitionAndOthersSeeNotFoundOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var owner = Token("synthetic-owner", "analyst", TenantA);
        var grantee = Token("synthetic-grantee", "operator", TenantA);
        var stranger = Token("synthetic-stranger", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, owner);

        using var unknown = await Send(client, HttpMethod.Post, Close(Guid.NewGuid()), owner, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        foreach (var bearer in new[] { stranger, grantee, Token("synthetic-owner", "analyst", TenantB) })
        {
            using var response = await Send(client, HttpMethod.Post, Close(inv), bearer, new { expectedVersion = 0 });
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.AreEqual(unknownBody, await response.Content.ReadAsStringAsync());
        }

        Assert.AreEqual(0, await db.TenantInvestigationStateEvents.AsNoTracking().CountAsync());

        // Positive control: the same request by an active grantee succeeds; once revoked it is 404 again.
        using var granted = await Send(client, HttpMethod.Put, Share(inv, "synthetic-grantee"), owner);
        Assert.AreEqual(HttpStatusCode.Created, granted.StatusCode);
        using var byGrantee = await Send(client, HttpMethod.Post, Close(inv), grantee, new { expectedVersion = 0 });
        Assert.AreEqual(HttpStatusCode.OK, byGrantee.StatusCode);
        Assert.AreEqual("synthetic-grantee", (await db.TenantInvestigationStateEvents.AsNoTracking().SingleAsync()).ActorSubject);
        using var reopened = await Send(client, HttpMethod.Post, Reopen(inv), owner, new { expectedVersion = 1 });
        Assert.AreEqual(HttpStatusCode.OK, reopened.StatusCode);
        using var revoked = await Send(client, HttpMethod.Delete, Share(inv, "synthetic-grantee"), owner);
        Assert.AreEqual(HttpStatusCode.NoContent, revoked.StatusCode);
        using var afterRevoke = await Send(client, HttpMethod.Post, Close(inv), grantee, new { expectedVersion = 2 });
        Assert.AreEqual(HttpStatusCode.NotFound, afterRevoke.StatusCode);
        Assert.AreEqual(2, await db.TenantInvestigationStateEvents.AsNoTracking().CountAsync());
    }

    [TestMethod]
    public async Task ConcurrentIdenticalClosesAppendExactlyOneEventOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var owner = Token("synthetic-owner", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, owner);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Send(client, HttpMethod.Post, Close(inv), owner, new { expectedVersion = 0 })));
        Assert.IsTrue(responses.All(r => r.StatusCode == HttpStatusCode.OK), "identical requests are replays, never errors");
        Assert.AreEqual(1, await db.TenantInvestigationStateEvents.AsNoTracking().CountAsync());
        Assert.AreEqual(1, await db.TenantAuditEvents.AsNoTracking().CountAsync(e => e.Action == "investigation.closed"));
        foreach (var response in responses) response.Dispose();
    }

    [TestMethod]
    public async Task DatabaseRefusesEventTamperingGapsAndClosedWritesOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        using var factory = new ApiFactory(cs);
        using var client = factory.CreateClient();
        await using var db = new VeritasAtlasDbContext(TenantInvestigationEvidenceItemTests.Options(cs));
        var verification = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var owner = Token("synthetic-owner", "analyst", TenantA);
        var inv = await TenantInvestigationEvidenceItemTests.CreateInvestigationAsync(client, owner);
        var item = (await new EfTenantInvestigationEvidenceStore(db, TimeProvider.System).CollectAsync(
            TenantA, inv, new EvidenceItemInput(verification, null, "synthetic"), CancellationToken.None)).Item!.Id;
        var store = new EfTenantInvestigationStore(db, TimeProvider.System);
        var at = DateTime.UtcNow;

        StateEvent(inv, 1, "open"); // refused: an investigation starts open
        await AssertSqlState(db, "23514");
        StateEvent(Guid.NewGuid(), 1, "closed"); // refused: unknown investigation
        await AssertSqlState(db, "23514");
        StateEvent(inv, 2, "closed"); // refused: version gap
        await AssertSqlState(db, "23514");
        db.TenantInvestigationBookmarks.Add(new TenantInvestigationBookmark
        {
            TenantId = TenantA, InvestigationId = inv, EvidenceItemId = item, Subject = "synthetic-pre", CreatedAtUtc = at
        });
        await db.SaveChangesAsync(); // an existing bookmark the raw delete below targets once closed
        StateEvent(inv, 1, "closed"); // positive control
        await db.SaveChangesAsync();
        StateEvent(inv, 2, "closed"); // refused: state did not change
        await AssertSqlState(db, "23514");

        foreach (var sql in new[]
                 {
                     "UPDATE tenant_investigation_state_events SET state = 'open'",
                     "DELETE FROM tenant_investigation_state_events",
                     "TRUNCATE tenant_investigation_state_events",
                 })
        {
            var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.AreEqual("55000", ex.SqlState, sql);
        }

        // Backstop beneath the application check: raw writes to guarded tables are refused while closed.
        db.TenantInvestigationBookmarks.Add(new TenantInvestigationBookmark
        {
            TenantId = TenantA, InvestigationId = inv, EvidenceItemId = item, Subject = "synthetic-raw", CreatedAtUtc = at
        });
        await AssertSqlState(db, "P0001");
        var delete = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => db.Database.ExecuteSqlRawAsync("DELETE FROM tenant_investigation_bookmarks"));
        Assert.AreEqual("P0001", delete.SqlState);

        // Positive control: reopening makes the same raw insert succeed.
        Assert.AreEqual(
            InvestigationTransitionOutcome.Transitioned,
            (await store.TransitionAsync(TenantA, inv, close: false, 1, "synthetic-owner", CancellationToken.None)).Outcome);
        db.TenantInvestigationBookmarks.Add(new TenantInvestigationBookmark
        {
            TenantId = TenantA, InvestigationId = inv, EvidenceItemId = item, Subject = "synthetic-raw", CreatedAtUtc = at
        });
        await db.SaveChangesAsync();

        void StateEvent(Guid investigation, int version, string state) =>
            db.TenantInvestigationStateEvents.Add(new TenantInvestigationStateEvent
            {
                TenantId = TenantA, InvestigationId = investigation, Version = version, State = state,
                ActorSubject = "synthetic-raw", CreatedAtUtc = at
            });
    }

    [TestMethod]
    public async Task CloseCommittingAfterTheCheckIsTranslatedToClosedOnEveryWritePathOverRealPostgres()
    {
        if (Database() is not { } cs) return;
        await TenantInvestigationEvidenceItemTests.ResetAsync(cs);
        var race = new CloseRaceInterceptor(cs);
        await using var db = new VeritasAtlasDbContext(
            new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(cs).AddInterceptors(race).Options);
        var verification = await TenantInvestigationEvidenceItemTests.SeedVerifiedAsync(db, TenantA, "a");
        var investigations = new EfTenantInvestigationStore(db, TimeProvider.System);
        var evidence = new EfTenantInvestigationEvidenceStore(db, TimeProvider.System);
        var notes = new EfTenantInvestigationAnnotationStore(db, TimeProvider.System);
        var marks = new EfTenantInvestigationBookmarkStore(db, TimeProvider.System);
        var shares = new EfTenantInvestigationShareStore(db, TimeProvider.System);
        var n = 0;

        async Task<Guid> NewInvestigation() =>
            (await investigations.CreateAsync(
                TenantA, new InvestigationInput($"race-{n++}", "Case", "synthetic-owner"), CancellationToken.None))
            .Investigation!.Id;

        async Task<Guid> NewItem(Guid inv) =>
            (await evidence.CollectAsync(
                TenantA, inv, new EvidenceItemInput(verification, null, "synthetic-owner"), CancellationToken.None)).Item!.Id;

        // Positive control: with no race the same calls succeed.
        var control = await NewInvestigation();
        var controlItem = await NewItem(control);
        Assert.AreEqual(AnnotationAddOutcome.Added,
            (await notes.AddAsync(TenantA, control, controlItem, new AnnotationInput("note", "synthetic-owner"), CancellationToken.None)).Outcome);
        Assert.AreEqual(BookmarkPutOutcome.Added,
            (await marks.PutAsync(TenantA, control, controlItem, "synthetic-owner", CancellationToken.None)).Outcome);
        Assert.AreEqual(ShareGrantOutcome.Granted,
            (await shares.GrantAsync(TenantA, control, "synthetic-owner", "synthetic-bob", CancellationToken.None)).Outcome);

        // Evidence collect.
        var inv = await NewInvestigation();
        race.Arm(inv);
        Assert.AreEqual(EvidenceItemCollectOutcome.InvestigationClosed,
            (await evidence.CollectAsync(TenantA, inv, new EvidenceItemInput(verification, null, "synthetic-owner"), CancellationToken.None)).Outcome);
        Assert.AreEqual(0, await db.TenantInvestigationEvidenceItems.AsNoTracking().CountAsync(x => x.InvestigationId == inv));

        // Annotation add.
        inv = await NewInvestigation();
        var item = await NewItem(inv);
        race.Arm(inv);
        Assert.AreEqual(AnnotationAddOutcome.InvestigationClosed,
            (await notes.AddAsync(TenantA, inv, item, new AnnotationInput("note", "synthetic-owner"), CancellationToken.None)).Outcome);
        Assert.AreEqual(0, await db.TenantInvestigationAnnotations.AsNoTracking().CountAsync(x => x.InvestigationId == inv));

        // Bookmark put and remove.
        inv = await NewInvestigation();
        item = await NewItem(inv);
        race.Arm(inv);
        Assert.AreEqual(BookmarkPutOutcome.InvestigationClosed,
            (await marks.PutAsync(TenantA, inv, item, "synthetic-owner", CancellationToken.None)).Outcome);
        inv = await NewInvestigation();
        item = await NewItem(inv);
        Assert.AreEqual(BookmarkPutOutcome.Added, (await marks.PutAsync(TenantA, inv, item, "synthetic-owner", CancellationToken.None)).Outcome);
        race.Arm(inv);
        Assert.AreEqual(BookmarkRemoveOutcome.InvestigationClosed,
            await marks.RemoveAsync(TenantA, inv, item, "synthetic-owner", CancellationToken.None));
        Assert.AreEqual(1, await db.TenantInvestigationBookmarks.AsNoTracking().CountAsync(x => x.InvestigationId == inv));

        // Share grant, revoke and re-grant.
        inv = await NewInvestigation();
        race.Arm(inv);
        Assert.AreEqual(ShareGrantOutcome.InvestigationClosed,
            (await shares.GrantAsync(TenantA, inv, "synthetic-owner", "synthetic-bob", CancellationToken.None)).Outcome);
        inv = await NewInvestigation();
        await shares.GrantAsync(TenantA, inv, "synthetic-owner", "synthetic-bob", CancellationToken.None);
        race.Arm(inv);
        Assert.AreEqual(ShareRevokeOutcome.InvestigationClosed,
            await shares.RevokeAsync(TenantA, inv, "synthetic-owner", "synthetic-bob", CancellationToken.None));
        Assert.AreEqual(1, await db.TenantInvestigationShares.AsNoTracking().CountAsync(x => x.InvestigationId == inv && x.RevokedAtUtc == null));
        inv = await NewInvestigation();
        await shares.GrantAsync(TenantA, inv, "synthetic-owner", "synthetic-bob", CancellationToken.None);
        await shares.RevokeAsync(TenantA, inv, "synthetic-owner", "synthetic-bob", CancellationToken.None);
        race.Arm(inv);
        Assert.AreEqual(ShareGrantOutcome.InvestigationClosed,
            (await shares.GrantAsync(TenantA, inv, "synthetic-owner", "synthetic-bob", CancellationToken.None)).Outcome);
        Assert.AreEqual(0, await db.TenantInvestigationShares.AsNoTracking().CountAsync(x => x.InvestigationId == inv && x.RevokedAtUtc == null));
        Assert.AreEqual(7, race.Fired, "every armed scenario really raced");
    }

    /// <summary>Once armed, closes the investigation on a second connection just before the next guarded write runs.</summary>
    private sealed class CloseRaceInterceptor(string connectionString)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private static readonly string[] Guarded =
        [
            "tenant_investigation_evidence_items", "tenant_investigation_annotations",
            "tenant_investigation_bookmarks", "tenant_investigation_shares"
        ];

        private Guid? _armed;
        public int Fired { get; private set; }

        public void Arm(Guid investigation) => _armed = investigation;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await CloseIfArmedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await CloseIfArmedAsync(command, cancellationToken);
            return result;
        }

        private async Task CloseIfArmedAsync(System.Data.Common.DbCommand command, CancellationToken cancellationToken)
        {
            var text = command.CommandText.TrimStart();
            var write = text.StartsWith("INSERT", StringComparison.Ordinal)
                        || text.StartsWith("UPDATE", StringComparison.Ordinal)
                        || text.StartsWith("DELETE", StringComparison.Ordinal);
            if (_armed is not { } investigation || !write || !Guarded.Any(text.Contains))
            {
                return;
            }

            _armed = null;
            Fired++;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var close = new NpgsqlCommand(
                "INSERT INTO tenant_investigation_state_events (tenant_id, investigation_id, version, state, actor_subject, created_at_utc) VALUES (@t, @i, 1, 'closed', 'synthetic-racer', now())",
                connection);
            close.Parameters.AddWithValue("t", TenantA);
            close.Parameters.AddWithValue("i", investigation);
            await close.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task AssertSqlState(VeritasAtlasDbContext db, string sqlState)
    {
        var ex = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.AreEqual(sqlState, ((PostgresException)ex.InnerException!).SqlState);
        db.ChangeTracker.Clear();
    }

    private static async Task<InvestigationDto> GetAsync(HttpClient client, Guid id, string bearer)
    {
        using var response = await Send(client, HttpMethod.Get, Detail(id), bearer);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvestigationDto>())!;
    }

    private sealed record InvestigationDto(Guid Id, string Title, string State, int Version);

    private sealed record ListDto(List<InvestigationDto> Items);

    private sealed record ItemDto(Guid Id);

    private sealed record ErrorDto(string Code);

    private static string? Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("VERITAS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("VERITAS_TEST_POSTGRES is not set.");
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Database += "_state";
        return builder.ConnectionString;
    }

    private sealed class CountingStore : ITenantInvestigationStore
    {
        private int _calls;
        public int Calls => _calls;

        public Task<InvestigationCreateResult> CreateAsync(
            Guid tenantId, InvestigationInput input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new InvestigationCreateResult(InvestigationCreateOutcome.IdempotencyConflict, null));
        }

        public Task<TenantInvestigationModel?> GetAsync(
            Guid tenantId, Guid investigationId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<TenantInvestigationModel?>(null);
        }

        public Task<IReadOnlyList<TenantInvestigationModel>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<TenantInvestigationModel>>([]);
        }

        public Task<InvestigationTransitionResult> TransitionAsync(
            Guid tenantId, Guid investigationId, bool close, int expectedVersion, string actorSubject,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new InvestigationTransitionResult(InvestigationTransitionOutcome.NotFound, null));
        }
    }

    private sealed class ApiFactory(string? connectionString = null, ITenantInvestigationStore? store = null)
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
