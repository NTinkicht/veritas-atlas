using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-16 slice 4 (#196): tenant-scoped durable publication revisions. Database-backed tests run against a real PostgreSQL
/// when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_publication</c> database) and are inconclusive otherwise.
/// </summary>
[TestClass]
public sealed class TenantPublicationRevisionStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    [TestMethod]
    public async Task InvalidArgumentsAreRefusedBeforeTheDatabaseIsTouched()
    {
        await using var db = new VeritasAtlasDbContext(new DbContextOptionsBuilder<VeritasAtlasDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1").Options);
        var store = new EfTenantPublicationRevisionStore(db, TimeProvider.System);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.CreateDraftAsync(Guid.Empty, "a", "d", true, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.CreateDraftAsync(TenantA, " ", "d", true, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.CreateDraftAsync(TenantA, "a", "", true, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GetAsync(Guid.Empty, Guid.NewGuid(), default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            store.TransitionAsync(Guid.Empty, Guid.NewGuid(), "a", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op", default));
    }

    [TestMethod]
    public async Task FullLifecycleIsVersionedAuditedAndBoundToTheApprovedDigest()
    {
        await using var db = await FreshAsync();
        var store = new EfTenantPublicationRevisionStore(db, TimeProvider.System);
        var rev = (await store.CreateDraftAsync(TenantA, "author", "sha256:aaa", true, default)).Id;

        var submit = await store.TransitionAsync(TenantA, rev, "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);
        Assert.AreEqual(PublicationTransitionOutcome.Applied, submit.Outcome);
        Assert.AreEqual(LifecycleState.Submitted, submit.Revision!.State);
        Assert.AreEqual(1, submit.Revision.Version);

        // Approved is not public: approval binds the digest but publication needs the separate Publisher action.
        var approve = await store.TransitionAsync(TenantA, rev, "reviewer", LifecycleRole.Reviewer, LifecycleAction.Approve, 1, "op2", default);
        Assert.AreEqual(LifecycleState.Approved, approve.Revision!.State);
        Assert.AreEqual("sha256:aaa", approve.Revision.ApprovedDigest);

        var publish = await store.TransitionAsync(TenantA, rev, "publisher", LifecycleRole.Publisher, LifecycleAction.Publish, 2, "op3", default);
        Assert.AreEqual(LifecycleState.Published, publish.Revision!.State);
        var withdraw = await store.TransitionAsync(TenantA, rev, "publisher", LifecycleRole.Publisher, LifecycleAction.Withdraw, 3, "op4", default);
        Assert.AreEqual(LifecycleState.Withdrawn, withdraw.Revision!.State);

        var audit = await db.TenantPublicationTransitions.AsNoTracking().Where(x => x.RevisionId == rev).OrderBy(x => x.Version).ToListAsync();
        CollectionAssert.AreEqual(new[] { "Submit", "Approve", "Publish", "Withdraw" }, audit.Select(x => x.Action).ToArray());
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4 }, audit.Select(x => x.Version).ToArray());
        Assert.AreEqual("reviewer", audit[1].ActorId);
        Assert.AreEqual("Approved", audit[1].ToState);
    }

    [TestMethod]
    public async Task DeniedAttemptsLeaveStateAndAuditUnchanged()
    {
        await using var db = await FreshAsync();
        var store = new EfTenantPublicationRevisionStore(db, TimeProvider.System);
        var rev = (await store.CreateDraftAsync(TenantA, "author", "sha256:aaa", false, default)).Id;
        await store.TransitionAsync(TenantA, rev, "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);

        Assert.AreEqual(LifecycleDenials.SelfApprovalDenied, (await store.TransitionAsync(
            TenantA, rev, "author", LifecycleRole.Reviewer, LifecycleAction.Approve, 1, "op2", default)).DenialCode);
        Assert.AreEqual(LifecycleDenials.RoleNotPermitted, (await store.TransitionAsync(
            TenantA, rev, "reviewer", LifecycleRole.Analyst, LifecycleAction.Approve, 1, "op2", default)).DenialCode);
        Assert.AreEqual(GuardDenials.StaleVersion, (await store.TransitionAsync(
            TenantA, rev, "reviewer", LifecycleRole.Reviewer, LifecycleAction.Approve, 0, "op2", default)).DenialCode);
        Assert.AreEqual(GuardDenials.OperationIdMissing, (await store.TransitionAsync(
            TenantA, rev, "reviewer", LifecycleRole.Reviewer, LifecycleAction.Approve, 1, " ", default)).DenialCode);
        // Publishing an unapproved revision: no approved digest is bound, so the guard denies before any state change.
        Assert.AreEqual(GuardDenials.ApprovalDigestMismatch, (await store.TransitionAsync(
            TenantA, rev, "publisher", LifecycleRole.Publisher, LifecycleAction.Publish, 1, "op2", default)).DenialCode);
        Assert.AreEqual(LifecycleDenials.InvalidTransition, (await store.TransitionAsync(
            TenantA, rev, "reviewer", LifecycleRole.Reviewer, LifecycleAction.Withdraw, 1, "op2", default)).DenialCode);

        await store.TransitionAsync(TenantA, rev, "reviewer", LifecycleRole.Reviewer, LifecycleAction.Approve, 1, "op2", default);
        // The policy does not permit public display: Approved can never be published.
        Assert.AreEqual(LifecycleDenials.PublicDisplayNotPermitted, (await store.TransitionAsync(
            TenantA, rev, "publisher", LifecycleRole.Publisher, LifecycleAction.Publish, 2, "op3", default)).DenialCode);

        var state = await store.GetAsync(TenantA, rev, default);
        Assert.AreEqual(LifecycleState.Approved, state!.State);
        Assert.AreEqual(2, state.Version);
        Assert.AreEqual(2, await db.TenantPublicationTransitions.CountAsync(x => x.RevisionId == rev));
    }

    [TestMethod]
    public async Task ReplayIsIdempotentOnlyForTheSameActorAndRole()
    {
        await using var db = await FreshAsync();
        var store = new EfTenantPublicationRevisionStore(db, TimeProvider.System);
        var rev = (await store.CreateDraftAsync(TenantA, "author", "sha256:aaa", true, default)).Id;
        await store.TransitionAsync(TenantA, rev, "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);

        var again = await store.TransitionAsync(TenantA, rev, "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);
        Assert.AreEqual(PublicationTransitionOutcome.Replayed, again.Outcome);
        Assert.AreEqual(1, again.Revision!.Version);

        var other = await store.TransitionAsync(TenantA, rev, "intruder", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);
        Assert.AreEqual(PublicationTransitionOutcome.Denied, other.Outcome);
        Assert.AreEqual(GuardDenials.OperationConflict, other.DenialCode);
        Assert.AreEqual(1, await db.TenantPublicationTransitions.CountAsync(x => x.RevisionId == rev));
    }

    [TestMethod]
    public async Task OtherTenantsRevisionIsIndistinguishableFromUnknown()
    {
        await using var db = await FreshAsync();
        var store = new EfTenantPublicationRevisionStore(db, TimeProvider.System);
        var rev = (await store.CreateDraftAsync(TenantA, "author", "sha256:aaa", true, default)).Id;

        Assert.IsNull(await store.GetAsync(TenantB, rev, default));
        var cross = await store.TransitionAsync(TenantB, rev, "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);
        var unknown = await store.TransitionAsync(TenantB, Guid.NewGuid(), "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);
        Assert.AreEqual(PublicationTransitionOutcome.NotFound, cross.Outcome);
        Assert.AreEqual(PublicationTransitionOutcome.NotFound, unknown.Outcome);
        Assert.AreEqual(LifecycleState.Draft, (await store.GetAsync(TenantA, rev, default))!.State);
    }

    [TestMethod]
    public async Task OnlyOneOfTwoRacingWritersApplies()
    {
        var cs = Database();
        await using (var seed = await FreshAsync())
        {
        }

        Guid rev;
        await using (var db = new VeritasAtlasDbContext(Options(cs!)))
        {
            rev = (await new EfTenantPublicationRevisionStore(db, TimeProvider.System).CreateDraftAsync(TenantA, "author", "sha256:aaa", true, default)).Id;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            await using var db = new VeritasAtlasDbContext(Options(cs!));
            return await new EfTenantPublicationRevisionStore(db, TimeProvider.System).TransitionAsync(
                TenantA, rev, "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "race-" + i, default);
        }));

        Assert.AreEqual(1, results.Count(r => r.Outcome == PublicationTransitionOutcome.Applied));
        Assert.IsTrue(results.Where(r => r.Outcome != PublicationTransitionOutcome.Applied)
            .All(r => r.Outcome is PublicationTransitionOutcome.ConcurrentUpdate or PublicationTransitionOutcome.Denied));
        await using var check = new VeritasAtlasDbContext(Options(cs!));
        Assert.AreEqual(1, await check.TenantPublicationTransitions.CountAsync(x => x.RevisionId == rev));
        Assert.AreEqual(1, (await new EfTenantPublicationRevisionStore(check, TimeProvider.System).GetAsync(TenantA, rev, default))!.Version);
    }

    [TestMethod]
    public async Task DatabaseRefusesBypassOfTheStore()
    {
        await using var db = await FreshAsync();
        var store = new EfTenantPublicationRevisionStore(db, TimeProvider.System);
        var rev = (await store.CreateDraftAsync(TenantA, "author", "sha256:aaa", true, default)).Id;
        await store.TransitionAsync(TenantA, rev, "author", LifecycleRole.Analyst, LifecycleAction.Submit, 0, "op1", default);

        foreach (var sql in new[]
                 {
                     "UPDATE tenant_publication_revisions SET state = 'Approved', version = version + 1, last_operation_id = 'x', last_action = 'Approve'",
                     "UPDATE tenant_publication_revisions SET state = 'Published', version = version + 2, approved_digest = content_digest, last_operation_id = 'x', last_action = 'Publish'",
                     "UPDATE tenant_publication_revisions SET content_digest = 'sha256:evil', version = version + 1, last_operation_id = 'x', last_action = 'Revise'",
                     "UPDATE tenant_publication_revisions SET author_id = 'other', version = version + 1, last_operation_id = 'x', last_action = 'Revise'",
                     "UPDATE tenant_publication_revisions SET policy_permits_public_display = false, version = version + 1, last_operation_id = 'x', last_action = 'Revise'",
                     "UPDATE tenant_publication_revisions SET state = 'Bogus', version = version + 1, last_operation_id = 'x', last_action = 'Revise'",
                     "DELETE FROM tenant_publication_revisions",
                     "TRUNCATE tenant_publication_revisions",
                     "UPDATE tenant_publication_transitions SET actor_id = 'forged'",
                     "DELETE FROM tenant_publication_transitions",
                     "TRUNCATE tenant_publication_transitions"
                 })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.IsTrue(ex.SqlState is "55000" or "23514" or "0A000", sql + " -> " + ex.SqlState);
        }

        // A revision that is Approved/Published without a bound approved digest violates the check constraint.
        var fresh = (await store.CreateDraftAsync(TenantA, "author", "sha256:bbb", true, default)).Id;
        var bad = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE tenant_publication_revisions SET state = 'Approved', version = 1, last_operation_id = 'x', last_action = 'Approve' WHERE id = {0}", fresh));
        Assert.AreEqual("23514", bad.SqlState);

        Assert.AreEqual(1, (await store.GetAsync(TenantA, rev, default))!.Version);
        Assert.AreEqual(1, await db.TenantPublicationTransitions.CountAsync());
    }

    private static async Task<VeritasAtlasDbContext> FreshAsync()
    {
        var cs = Database()!;
        await using (var reset = new VeritasAtlasDbContext(Options(cs)))
        {
            await reset.Database.EnsureDeletedAsync();
            await reset.Database.MigrateAsync();
        }

        return new VeritasAtlasDbContext(Options(cs));
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
        builder.Database += "_publication";
        return builder.ConnectionString;
    }

    private static DbContextOptions<VeritasAtlasDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options;
}
