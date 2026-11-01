using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Extensions;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-04 slice 5b (#121): the fetch policy registry service applies
/// connector admission to a first policy and the source change policy to every
/// later one, appends each admitted revision with its audit event atomically,
/// and is tenant-scoped. Runs against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_fetchpolicyregistry</c>
/// database) and is inconclusive otherwise.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenantSourceFetchPolicyRegistryTests
{
    private const string Actor = "synthetic-registrar";
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");

    private static SourceFetchPolicy Policy(
        string version = "2026-10.1",
        string jurisdiction = "dz",
        string sourceType = "court_record",
        string[]? hosts = null,
        string[]? schemes = null,
        int[]? ports = null,
        int maxRedirects = 3,
        bool httpException = false) =>
        new(version, jurisdiction, sourceType,
            hosts ?? ["records.example.dz", "archive.example.dz"],
            schemes ?? ["https"], ports ?? [443], maxRedirects, httpException);

    [TestMethod]
    public async Task FirstAdmittedPolicyIsRevisionOneWithItsAuditEvent()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = Context(cs);
        var source = await SourceAsync(db, TenantA, "s1");
        var registry = Registry(db);

        Assert.IsNull(await registry.GetCurrentAsync(TenantA, source, CancellationToken.None));
        var result = await registry.ProposeAsync(TenantA, source, Policy(), false, Actor, CancellationToken.None);

        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Created, result.Outcome);
        Assert.AreEqual(0, result.Reasons.Count);
        Assert.AreEqual(1, result.Policy!.Revision);
        Assert.IsFalse(result.Policy.WideningApproved);
        var current = await registry.GetCurrentAsync(TenantA, source, CancellationToken.None);
        Assert.AreEqual(result.Policy.Id, current!.Id);
        Assert.AreEqual("2026-10.1", current.Policy.PolicyVersion);
        CollectionAssert.AreEqual(new[] { "records.example.dz", "archive.example.dz" }, current.Policy.AllowedHosts.ToArray());
        CollectionAssert.AreEqual(new[] { 443 }, current.Policy.AllowedPorts.ToArray());
        Assert.AreEqual(3, current.Policy.MaxRedirects);

        var audit = await db.TenantAuditEvents.AsNoTracking()
            .SingleAsync(x => x.Action == EfTenantSourceFetchPolicyRegistry.RegisteredAction);
        Assert.AreEqual(TenantA, audit.TenantId);
        Assert.AreEqual("tenant_source", audit.EntityType);
        Assert.AreEqual(source.ToString("D"), audit.EntityId);
        Assert.AreEqual(Actor, audit.ActorSubject);
        Assert.AreEqual($"tenant_source.fetch_policy:{source:D}:1", audit.EventKey);
    }

    [TestMethod]
    public async Task InvalidFirstPolicyIsRefusedWithAdmissionReasonsAndWritesNothing()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = Context(cs);
        var source = await SourceAsync(db, TenantA, "s1");
        var auditBefore = await db.TenantAuditEvents.CountAsync();

        var result = await Registry(db).ProposeAsync(TenantA, source,
            Policy(hosts: ["127.0.0.1"], schemes: ["http"]), true, Actor, CancellationToken.None);

        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Refused, result.Outcome);
        Assert.IsNull(result.Policy);
        CollectionAssert.AreEqual(new[] { "invalid_allowed_host", "http_without_exception" }, result.Reasons.ToArray());
        Assert.AreEqual(0, await db.TenantSourceFetchPolicies.CountAsync());
        Assert.AreEqual(auditBefore, await db.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task RevisionsFollowTheSourceChangePolicy()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = Context(cs);
        var source = await SourceAsync(db, TenantA, "s1");
        var registry = Registry(db);
        Task<SourceFetchPolicyProposalResult> Propose(SourceFetchPolicy policy, bool approved = false) =>
            registry.ProposeAsync(TenantA, source, policy, approved, Actor, CancellationToken.None);

        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Created, (await Propose(Policy())).Outcome);

        // Identical resubmission, even with approval: a no-op, nothing written.
        var same = await Propose(Policy(), approved: true);
        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Unchanged, same.Outcome);
        Assert.AreEqual(1, same.Policy!.Revision);

        // Narrowing needs no approval.
        var narrowed = await Propose(Policy("2026-10.2", hosts: ["records.example.dz"], maxRedirects: 1));
        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Created, narrowed.Outcome);
        Assert.AreEqual(2, narrowed.Policy!.Revision);
        Assert.IsFalse(narrowed.Policy.WideningApproved);

        // Widening without approval is refused; reusing an old version,
        // not bumping it and re-classifying the source are refused too.
        foreach (var (policy, reasons) in new (SourceFetchPolicy, string[])[]
                 {
                     (Policy("2026-10.3", hosts: ["records.example.dz", "new.example.dz"], maxRedirects: 1),
                         ["widening_requires_approval"]),
                     (Policy("2026-10.1", hosts: ["records.example.dz"], maxRedirects: 0), ["policy_version_reused"]),
                     (Policy("2026-10.2", hosts: ["records.example.dz"], maxRedirects: 0), ["policy_version_not_bumped"]),
                     (Policy("2026-10.3", jurisdiction: "fr", hosts: ["records.example.dz"], maxRedirects: 1),
                         ["jurisdiction_change_forbidden"]),
                     (Policy("2026-10.3", sourceType: "company_record", hosts: ["records.example.dz"], maxRedirects: 1),
                         ["source_type_change_forbidden"]),
                     (Policy("2026-10.3", hosts: ["localhost"], maxRedirects: 1), ["invalid_allowed_host"]),
                 })
        {
            var refused = await Propose(policy);
            Assert.AreEqual(SourceFetchPolicyProposalOutcome.Refused, refused.Outcome, string.Join(',', reasons));
            CollectionAssert.AreEqual(reasons, refused.Reasons.ToArray());
        }

        // With explicit approval the widening is admitted and recorded.
        var widened = await Propose(
            Policy("2026-10.3", hosts: ["records.example.dz", "new.example.dz"], schemes: ["https", "http"],
                ports: [443, 80], maxRedirects: 1, httpException: true),
            approved: true);
        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Created, widened.Outcome);
        Assert.AreEqual(3, widened.Policy!.Revision);
        Assert.IsTrue(widened.Policy.WideningApproved);

        var rows = await db.TenantSourceFetchPolicies.AsNoTracking().OrderBy(x => x.Revision).ToListAsync();
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, rows.Select(x => x.Revision).ToArray());
        CollectionAssert.AreEqual(new[] { false, false, true }, rows.Select(x => x.WideningApproved).ToArray());
        var actions = await db.TenantAuditEvents.AsNoTracking()
            .Where(x => x.EntityId == source.ToString("D") && x.Action.StartsWith("tenant_source.fetch_policy"))
            .OrderBy(x => x.EventKey).Select(x => x.Action).ToListAsync();
        CollectionAssert.AreEqual(
            new[]
            {
                EfTenantSourceFetchPolicyRegistry.RegisteredAction,
                EfTenantSourceFetchPolicyRegistry.RevisedAction,
                EfTenantSourceFetchPolicyRegistry.RevisedAction
            },
            actions);
        Assert.AreEqual(3, (await registry.GetCurrentAsync(TenantA, source, CancellationToken.None))!.Revision);
    }

    [TestMethod]
    public async Task AnotherTenantsSourceIsIndistinguishableFromAMissingOne()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = Context(cs);
        var sourceA = await SourceAsync(db, TenantA, "s1");
        var registry = Registry(db);
        Assert.AreEqual(SourceFetchPolicyProposalOutcome.Created,
            (await registry.ProposeAsync(TenantA, sourceA, Policy(), false, Actor, CancellationToken.None)).Outcome);
        var auditBefore = await db.TenantAuditEvents.CountAsync();

        foreach (var (tenant, source) in new[] { (TenantB, sourceA), (TenantA, Guid.NewGuid()) })
        {
            var result = await registry.ProposeAsync(tenant, source, Policy("2026-10.9"), true, Actor, CancellationToken.None);
            Assert.AreEqual(SourceFetchPolicyProposalOutcome.SourceNotFound, result.Outcome);
            Assert.IsNull(result.Policy);
            Assert.IsNull(await registry.GetCurrentAsync(tenant, source, CancellationToken.None));
        }

        Assert.AreEqual(1, await db.TenantSourceFetchPolicies.CountAsync());
        Assert.AreEqual(auditBefore, await db.TenantAuditEvents.CountAsync());
    }

    [TestMethod]
    public async Task InvalidTenantOrActorIsRefusedBeforeTheDatabase()
    {
        await using var db = Context("Host=127.0.0.1;Port=1;Database=unused;Username=none;Password=none");
        var registry = Registry(db);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            registry.ProposeAsync(Guid.Empty, Guid.NewGuid(), Policy(), false, Actor, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            registry.GetCurrentAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None));
        foreach (var actor in new[] { "", "  ", "bad\nactor", new string('a', TenantAuditEventLimits.MaxSubjectLength + 1) })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                registry.ProposeAsync(TenantA, Guid.NewGuid(), Policy(), false, actor, CancellationToken.None));
        }
    }

    [TestMethod]
    public async Task ConcurrentWritersNeverCommitTheSameRevisionTwice()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        Guid source;
        await using (var setup = Context(cs))
        {
            source = await SourceAsync(setup, TenantA, "s1");
            await Registry(setup).ProposeAsync(TenantA, source, Policy(), false, Actor, CancellationToken.None);
        }

        // Each writer narrows to a distinct new version, so any interleaving is
        // admissible; only same-revision races may lose, and must lose cleanly.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = Context(cs);
            return await Registry(db).ProposeAsync(TenantA, source,
                Policy($"2026-10.c{i}", hosts: ["records.example.dz"], maxRedirects: 2), false, Actor,
                CancellationToken.None);
        }));

        Assert.IsTrue(results.All(r => r.Outcome is SourceFetchPolicyProposalOutcome.Created
            or SourceFetchPolicyProposalOutcome.Conflict
            or SourceFetchPolicyProposalOutcome.Refused));
        await using var check = Context(cs);
        var revisions = await check.TenantSourceFetchPolicies.AsNoTracking()
            .OrderBy(x => x.Revision).Select(x => x.Revision).ToListAsync();
        var created = results.Count(r => r.Outcome == SourceFetchPolicyProposalOutcome.Created);
        Assert.IsTrue(created >= 1);
        CollectionAssert.AreEqual(Enumerable.Range(1, created + 1).ToArray(), revisions.ToArray());
        Assert.AreEqual(created + 1, await check.TenantAuditEvents.CountAsync(x =>
            x.Action.StartsWith("tenant_source.fetch_policy")));
    }

    [TestMethod]
    public void RegistryIsRegisteredForInjection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=unused"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddVeritasAtlasInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsInstanceOfType<EfTenantSourceFetchPolicyRegistry>(
            scope.ServiceProvider.GetRequiredService<ITenantSourceFetchPolicyRegistry>());
    }

    private static EfTenantSourceFetchPolicyRegistry Registry(VeritasAtlasDbContext db) =>
        new(db, TimeProvider.System);

    private static VeritasAtlasDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<VeritasAtlasDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<Guid> SourceAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://records.example.dz/" + key, "synthetic-seeder", CancellationToken.None);
        return source!.Id;
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
        builder.Database += "_fetchpolicyregistry";
        return builder.ConnectionString;
    }

    private static async Task ResetAsync(string connectionString)
    {
        await using var db = Context(connectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }
}
