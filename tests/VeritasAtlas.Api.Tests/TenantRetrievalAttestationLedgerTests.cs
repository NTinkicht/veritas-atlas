using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-03 slice 12 (#107): append-only tenant-scoped retrieval attestation
/// records. Database-backed tests run against a real PostgreSQL when
/// <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_attestledger</c> database) and
/// are inconclusive otherwise; input validation runs everywhere.
/// </summary>
[TestClass]
public sealed class TenantRetrievalAttestationLedgerTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime AttestedAt = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("<html>public record 1</html>");
    private const string Actor = "synthetic-retriever";

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        // No reachable server: a refusal must happen before any connection.
        await using var db = new VeritasAtlasDbContext(new DbContextOptionsBuilder<VeritasAtlasDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1").Options);
        var ledger = new EfTenantRetrievalAttestationLedger(db, TimeProvider.System);
        var r = Guid.NewGuid();
        var longIdentity = new string('a', TenantRetrievalAttestationLimits.MaxIdentityLength + 1);
        var cases = new (Guid Tenant, RetrievalAttestationInput Input, string Actor)[]
        {
            (Guid.Empty, Input(r), Actor),
            (TenantA, Input(Guid.Empty), Actor),
            (TenantA, Input(r) with { AttestationKey = " " }, Actor),
            (TenantA, Input(r) with { AttestationKey = new string('k', TenantRetrievalAttestationLimits.MaxAttestationKeyLength + 1) }, Actor),
            (TenantA, Input(r) with { RetrieverIdentity = "" }, Actor),
            (TenantA, Input(r) with { RetrieverIdentity = " veritas-retriever" }, Actor),
            (TenantA, Input(r) with { RetrieverIdentity = longIdentity }, Actor),
            (TenantA, Input(r) with { PublisherIdentity = "court " }, Actor),
            (TenantA, Input(r) with { PublisherIdentity = "court\nregistry" }, Actor),
            (TenantA, Input(r) with { BindingReference = "reg:1\u0085" }, Actor),
            (TenantA, Input(r) with { BindingReference = new string('b', TenantRetrievalAttestationLimits.MaxBindingReferenceLength + 1) }, Actor),
            (TenantA, Input(r) with { Jurisdiction = "" }, Actor),
            (TenantA, Input(r) with { Jurisdiction = "DZ" }, Actor),
            (TenantA, Input(r) with { Jurisdiction = "dz\n" }, Actor),
            (TenantA, Input(r) with { Jurisdiction = new string('d', TenantRetrievalAttestationLimits.MaxJurisdictionLength + 1) }, Actor),
            (TenantA, Input(r) with { AttestedAtUtc = DateTime.SpecifyKind(AttestedAt, DateTimeKind.Local) }, Actor),
            (TenantA, Input(r) with { AttestedAtUtc = DateTime.SpecifyKind(AttestedAt, DateTimeKind.Unspecified) }, Actor),
            (TenantA, Input(r) with { BindingMechanism = (PublisherBindingMechanism)9 }, Actor),
            (TenantA, Input(r) with { AuthenticationResult = (SourceAuthenticationResult)9 }, Actor),
            (TenantA, Input(r) with { SourceType = (AttestedSourceType)9 }, Actor),
            (TenantA, Input(r), ""),
            (TenantA, Input(r), "a\u0000b"),
            (TenantA, Input(r), new string('a', TenantAuditEventLimits.MaxSubjectLength + 1)),
        };
        foreach (var (tenant, input, actor) in cases)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ledger.RecordAsync(tenant, input, actor, CancellationToken.None), input + " / " + actor);
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ledger.ListForRetrievalAsync(Guid.Empty, r, CancellationToken.None));
    }

    [TestMethod]
    public async Task RecordsAndReadsBackTheExactFieldsWithinTheTenant()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrieval = await RetrievalAsync(db, TenantA, "r1");
        var ledger = new EfTenantRetrievalAttestationLedger(db, TimeProvider.System);

        var input = Input(retrieval);
        var signed = Input(retrieval) with
        {
            AttestationKey = "att-2",
            BindingMechanism = PublisherBindingMechanism.PublisherSignature,
            BindingReference = "sig:sha256:abc",
            AuthenticationResult = SourceAuthenticationResult.Failed,
            SourceType = AttestedSourceType.OtherPublicRecord,
            AttestedAtUtc = AttestedAt.AddMinutes(5)
        };
        var first = await ledger.RecordAsync(TenantA, input, Actor, CancellationToken.None);
        var second = await ledger.RecordAsync(TenantA, signed, Actor, CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, first.Outcome);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, second.Outcome);
        var a = first.Attestation!;
        Assert.AreEqual(
            (retrieval, "att-1", "veritas-retriever/1", "Cour supreme", PublisherBindingMechanism.SourceRegistry,
                "registry:dz-courts#7", SourceAuthenticationResult.Authenticated, "dz", AttestedSourceType.CourtRecord, AttestedAt),
            (a.RawRetrievalId, a.AttestationKey, a.RetrieverIdentity, a.PublisherIdentity, a.BindingMechanism,
                a.BindingReference, a.AuthenticationResult, a.Jurisdiction, a.SourceType, a.AttestedAtUtc));

        var stored = await db.TenantRetrievalAttestations.AsNoTracking().OrderBy(x => x.AttestedAtUtc).ToListAsync();
        CollectionAssert.AreEqual(new[] { "source_registry", "publisher_signature" }, stored.Select(x => x.BindingMechanism).ToArray());
        CollectionAssert.AreEqual(new[] { "authenticated", "failed" }, stored.Select(x => x.AuthenticationResult).ToArray());
        CollectionAssert.AreEqual(new[] { "court_record", "other_public_record" }, stored.Select(x => x.SourceType).ToArray());
        // Recording is not verifying: nothing is minted on the raw retrieval.
        Assert.IsTrue((await db.TenantRawRetrievals.AsNoTracking().ToListAsync()).All(x => x.EvidenceReference is null));

        var listed = await ledger.ListForRetrievalAsync(TenantA, retrieval, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { first.Attestation, second.Attestation }, listed.ToArray());
        Assert.AreEqual(0, (await ledger.ListForRetrievalAsync(TenantB, retrieval, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task CrossTenantAndUnknownRetrievalsAreNotFound()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrievalB = await RetrievalAsync(db, TenantB, "r-b");
        var ledger = new EfTenantRetrievalAttestationLedger(db, TimeProvider.System);

        var crossTenant = await ledger.RecordAsync(TenantA, Input(retrievalB), Actor, CancellationToken.None);
        var unknown = await ledger.RecordAsync(TenantA, Input(Guid.NewGuid()) with { AttestationKey = "k2" }, Actor, CancellationToken.None);
        Assert.AreEqual(new RetrievalAttestationRecordResult(RetrievalAttestationRecordOutcome.RetrievalNotFound, null), crossTenant);
        Assert.AreEqual(crossTenant, unknown);
        Assert.AreEqual(0, await db.TenantRetrievalAttestations.CountAsync());
        Assert.AreEqual(0, await db.TenantAuditEvents.CountAsync(e => e.EntityType == EfTenantRetrievalAttestationLedger.AuditEntityType));
    }

    [TestMethod]
    public async Task AttestationKeyReplaysOnlyTheSameAttestationAndWritesOneAuditEvent()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrieval = await RetrievalAsync(db, TenantA, "r1");
        var retrieval2 = await RetrievalAsync(db, TenantA, "r2");
        var retrievalB = await RetrievalAsync(db, TenantB, "r-b");
        var ledger = new EfTenantRetrievalAttestationLedger(db, TimeProvider.System);

        var first = await ledger.RecordAsync(TenantA, Input(retrieval), Actor, CancellationToken.None);
        var replay = await ledger.RecordAsync(TenantA, Input(retrieval), "another-actor", CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Attestation, replay.Attestation);

        var conflicts = new[]
        {
            Input(retrieval2),
            Input(retrieval) with { RetrieverIdentity = "veritas-retriever/2" },
            Input(retrieval) with { PublisherIdentity = "Other court" },
            Input(retrieval) with { BindingMechanism = PublisherBindingMechanism.PublisherSignature },
            Input(retrieval) with { BindingReference = "registry:dz-courts#8" },
            Input(retrieval) with { AuthenticationResult = SourceAuthenticationResult.Unauthenticated },
            Input(retrieval) with { Jurisdiction = "fr" },
            Input(retrieval) with { SourceType = AttestedSourceType.Statute },
            Input(retrieval) with { AttestedAtUtc = AttestedAt.AddSeconds(1) },
        };
        foreach (var conflict in conflicts)
        {
            Assert.AreEqual(
                new RetrievalAttestationRecordResult(RetrievalAttestationRecordOutcome.IdempotencyConflict, null),
                await ledger.RecordAsync(TenantA, conflict, Actor, CancellationToken.None), conflict.ToString());
        }

        // The same key in another tenant is a distinct attestation.
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded,
            (await ledger.RecordAsync(TenantB, Input(retrievalB), Actor, CancellationToken.None)).Outcome);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(2, await check.TenantRetrievalAttestations.CountAsync());
        var audit = await check.TenantAuditEvents.AsNoTracking()
            .SingleAsync(e => e.TenantId == TenantA && e.EntityType == EfTenantRetrievalAttestationLedger.AuditEntityType);
        Assert.AreEqual($"tenant_retrieval_attestation.recorded:{first.Attestation!.Id:D}", audit.EventKey);
        Assert.AreEqual("tenant_retrieval_attestation.recorded", audit.Action);
        Assert.AreEqual(first.Attestation.Id.ToString("D"), audit.EntityId);
        Assert.AreEqual(Actor, audit.ActorSubject);
        Assert.AreEqual(first.Attestation.CreatedAtUtc, DateTime.SpecifyKind(audit.OccurredAtUtc, DateTimeKind.Utc));
        // The event carries no attestation content and no retrieval id.
        var fields = string.Join("|", audit.EventKey, audit.Action, audit.EntityType, audit.EntityId, audit.ActorSubject);
        foreach (var leaked in new[] { "veritas-retriever/1", "Cour", "registry:", "source_registry", "authenticated", "court_record", retrieval.ToString("D") })
        {
            Assert.IsFalse(fields.Contains(leaked, StringComparison.Ordinal), fields);
        }
    }

    [TestMethod]
    public async Task ConcurrentSameKeyWritesYieldOneRowAndOneAuditEvent()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        var options = Options(connectionString);
        Guid retrieval;
        await using (var setup = new VeritasAtlasDbContext(options))
        {
            retrieval = await RetrievalAsync(setup, TenantA, "race");
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = new VeritasAtlasDbContext(options);
            return await new EfTenantRetrievalAttestationLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Input(retrieval), $"synthetic-worker-{i}", CancellationToken.None);
        }));

        var winner = results.Single(r => r.Outcome == RetrievalAttestationRecordOutcome.Recorded).Attestation!;
        Assert.IsTrue(results.All(r => r.Attestation == winner));
        await using var check = new VeritasAtlasDbContext(options);
        Assert.AreEqual(1, await check.TenantRetrievalAttestations.CountAsync());
        var audit = await check.TenantAuditEvents.AsNoTracking()
            .SingleAsync(e => e.EntityType == EfTenantRetrievalAttestationLedger.AuditEntityType);
        Assert.AreEqual(winner.Id.ToString("D"), audit.EntityId);
    }

    [TestMethod]
    public async Task FailedAuditWriteRollsBackTheAttestation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrieval = await RetrievalAsync(db, TenantA, "r1");
        // Test-only fault in this per-test database: every later audit insert fails.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION test_refuse_audit() RETURNS trigger LANGUAGE plpgsql AS "
            + "$$BEGIN RAISE EXCEPTION 'audit unavailable' USING ERRCODE = 'P0001'; END$$");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER test_refuse_audit BEFORE INSERT ON tenant_audit_events "
            + "FOR EACH ROW EXECUTE FUNCTION test_refuse_audit()");

        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
            new EfTenantRetrievalAttestationLedger(db, TimeProvider.System)
                .RecordAsync(TenantA, Input(retrieval), Actor, CancellationToken.None));
        Assert.AreEqual("P0001", (error.InnerException as PostgresException)?.SqlState);

        await using var check = new VeritasAtlasDbContext(Options(connectionString));
        Assert.AreEqual(0, await check.TenantRetrievalAttestations.CountAsync());
        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync(e => e.EntityType == EfTenantRetrievalAttestationLedger.AuditEntityType));
    }

    [TestMethod]
    public async Task SchemaRefusesInvalidVocabulariesShapesCrossTenantLinksAndMutation()
    {
        if (Database() is not { } connectionString) return;
        await ResetAsync(connectionString);
        await using var db = new VeritasAtlasDbContext(Options(connectionString));
        var retrievalA = await RetrievalAsync(db, TenantA, "r-a");

        Task Insert(Guid tenant, Guid retrieval, string retriever, string mechanism, string reference, string result, string jurisdiction, string sourceType) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenant_retrieval_attestations (tenant_id, id, raw_retrieval_id, attestation_key, retriever_identity, publisher_identity, binding_mechanism, binding_reference, authentication_result, jurisdiction, source_type, attested_at_utc, created_at_utc) VALUES ({tenant}, {Guid.NewGuid()}, {retrieval}, {Guid.NewGuid().ToString()}, {retriever}, {"Cour supreme"}, {mechanism}, {reference}, {result}, {jurisdiction}, {sourceType}, {AttestedAt}, {AttestedAt})");

        await Insert(TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "court_record");
        await Insert(TenantA, retrievalA, "veritas-retriever/1", "publisher_signature", "sig:1", "unauthenticated", "dz.alger", "filing");

        var refused = new (Guid Tenant, Guid Retrieval, string Retriever, string Mechanism, string Reference, string Result, string Jurisdiction, string SourceType)[]
        {
            (TenantA, retrievalA, "veritas-retriever/1", "tls_only", "registry:1", "authenticated", "dz", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "verified", "dz", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "news"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "DZ", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "", "court_record"),
            (TenantA, retrievalA, " veritas", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
            (TenantA, retrievalA, "veritas\u0007", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
            (TenantA, retrievalA, "veritas-retriever/1", "source_registry", "", "authenticated", "dz", "court_record"),
            // Another tenant's retrieval, an empty tenant.
            (TenantB, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
            (Guid.Empty, retrievalA, "veritas-retriever/1", "source_registry", "registry:1", "authenticated", "dz", "court_record"),
        };
        foreach (var row in refused)
        {
            await Assert.ThrowsExactlyAsync<PostgresException>(
                () => Insert(row.Tenant, row.Retrieval, row.Retriever, row.Mechanism, row.Reference, row.Result, row.Jurisdiction, row.SourceType),
                row.ToString());
        }

        foreach (var mutation in new[]
                 {
                     "UPDATE tenant_retrieval_attestations SET authentication_result = 'authenticated'",
                     "DELETE FROM tenant_retrieval_attestations",
                     "TRUNCATE tenant_retrieval_attestations"
                 })
        {
            var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(mutation));
            Assert.AreEqual("55000", error.SqlState, mutation);
        }

        Assert.AreEqual(2, await db.TenantRetrievalAttestations.CountAsync());
    }

    private static RetrievalAttestationInput Input(Guid retrieval) =>
        new(retrieval, "att-1", "veritas-retriever/1", "Cour supreme", PublisherBindingMechanism.SourceRegistry,
            "registry:dz-courts#7", SourceAuthenticationResult.Authenticated, "dz", AttestedSourceType.CourtRecord, AttestedAt);

    private static async Task<Guid> RetrievalAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var result = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, "https://example.test/a", AttestedAt, Bytes), "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, result.Outcome);
        return result.Record!.Id;
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
        builder.Database += "_attestledger";
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
}
