using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-07 slice 3 (#174): the verified-and-answerable gate. Database tests run on a real
/// PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_evidenceverify</c> database).
/// All fixtures are hand-written and SYNTHETIC.
/// </summary>
[TestClass]
public sealed class EvidenceVerificationServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Actor = "synthetic-verifier";
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private const string Doc =
        "<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Section one.</P1><P1 id=\"s2\">Section two.</P1></Legislation>";
    private static readonly EvidenceClassificationExpectation Expected = new("gb-uk", "statute");

    /// <summary>Test double standing in for the future trusted verifier: echoes the claim.</summary>
    private sealed class EchoVerifier(bool verified = true) : IPublisherBindingVerifier
    {
        public Task<PublisherBindingVerification> VerifyAsync(AttestationClaim c, CancellationToken ct) =>
            Task.FromResult(new PublisherBindingVerification(
                c.PublisherIdentity, c.BindingMechanism, c.BindingReference, c.FinalUri, c.ContentSha256, verified));
    }

    private sealed class ThrowingVerifier : IPublisherBindingVerifier
    {
        public Task<PublisherBindingVerification> VerifyAsync(AttestationClaim c, CancellationToken ct) =>
            throw new InvalidOperationException("boundary down");
    }

    [TestMethod]
    public async Task ProductionVerifierNeverVerifiesAnything()
    {
        var claim = new AttestationClaim("r", "p", "source_registry", "ref", "authenticated", new string('a', 64), Uri, "gb-uk", "statute", At, At);
        var v = await new FailClosedPublisherBindingVerifier().VerifyAsync(claim, CancellationToken.None);
        Assert.IsFalse(v.Verified);
        var decision = RetrievalAttestationBinding.Evaluate(claim, new AttestedRetrieval(claim.ContentSha256, Uri, At), new AttestationExpectation("gb-uk", "statute", At.AddHours(1)), v);
        Assert.AreEqual("publisher_binding_unverified", decision.Reason);
    }

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var service = new EfEvidenceVerificationService(null!, new EchoVerifier(), TimeProvider.System);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.VerifyAsync(Guid.Empty, Guid.NewGuid(), Expected, Actor, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.VerifyAsync(TenantA, Guid.Empty, Expected, Actor, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.VerifyAsync(TenantA, Guid.NewGuid(), Expected, "a\nb", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.VerifyAsync(TenantA, Guid.NewGuid(), null!, Actor, CancellationToken.None));
        Assert.IsFalse(await service.IsVerifiedAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task VerifiesOnceWritesAuditAndReplaysTheOriginalRow()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var (run, attestation) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated());
        var service = Service(db, new EchoVerifier());

        var first = await service.VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.Verified, first.Outcome);
        Assert.AreEqual(attestation, first.Verification!.AttestationId);
        Assert.IsTrue(first.Verification.ParentEvidenceRef.StartsWith("ev1:", StringComparison.Ordinal));
        Assert.IsTrue(await service.IsVerifiedAsync(TenantA, run, CancellationToken.None));

        var replay = await service.VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Verification.Id, replay.Verification!.Id);

        await using var check = new VeritasAtlasDbContext(Options(cs));
        Assert.AreEqual(1, await check.TenantEvidenceVerifications.CountAsync());
        Assert.AreEqual(1, await check.TenantAuditEvents.CountAsync(e => e.Action == EfEvidenceVerificationService.RecordedAction));
        // Verification marks the run; it does not touch the unverified evidence-reference slots.
        Assert.IsNull((await check.TenantParseRuns.AsNoTracking().SingleAsync()).EvidenceReference);
    }

    [TestMethod]
    public async Task TheFailClosedVerifierYieldsNoVerifiedRow()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var (run, _) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated());
        var service = Service(db, new FailClosedPublisherBindingVerifier());

        var result = await service.VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.Refused, result.Outcome);
        Assert.AreEqual("publisher_binding_unverified", result.RefusalReason);
        await AssertNothingWrittenAsync(cs, run, service);
    }

    [TestMethod]
    public async Task AFailingVerifierFailsClosed()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var (run, _) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated());
        var service = Service(db, new ThrowingVerifier());
        var result = await service.VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None);
        Assert.AreEqual("publisher_binding_unverified", result.RefusalReason);
        await AssertNothingWrittenAsync(cs, run, service);
    }

    [TestMethod]
    public async Task UnauthenticatedAndFailedAttestationsNeverVerify()
    {
        if (Database() is not { } cs) return;
        foreach (var auth in new[] { SourceAuthenticationResult.Unauthenticated, SourceAuthenticationResult.Failed })
        {
            await ResetAsync(cs);
            await using var db = new VeritasAtlasDbContext(Options(cs));
            var (run, _) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated() with { AuthenticationResult = auth });
            var service = Service(db, new EchoVerifier());
            var result = await service.VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None);
            Assert.AreEqual("not_authenticated", result.RefusalReason, auth.ToString());
            await AssertNothingWrittenAsync(cs, run, service);
        }
    }

    [TestMethod]
    public async Task ClassificationMismatchAgainstTheTrustedExpectationIsRefused()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var (run, _) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated());
        var service = Service(db, new EchoVerifier());

        var jurisdiction = await service.VerifyAsync(TenantA, run, Expected with { Jurisdiction = "dz" }, Actor, CancellationToken.None);
        Assert.AreEqual("jurisdiction_mismatch", jurisdiction.RefusalReason);
        var type = await service.VerifyAsync(TenantA, run, Expected with { SourceType = "regulation" }, Actor, CancellationToken.None);
        Assert.AreEqual("source_type_mismatch", type.RefusalReason);
        await AssertNothingWrittenAsync(cs, run, service);
    }

    [TestMethod]
    public async Task FailedRunsMissingAttestationsAndUnknownRunsNeverVerify()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var service = Service(db, new EchoVerifier());

        var bad = await RetrievalAsync(db, TenantA, "bad", Uri, "<not-legislation/>");
        var failedRun = (await new EfEvidenceParseService(db, TimeProvider.System).ParseAsync(TenantA, bad, Actor, CancellationToken.None)).Run!.Id;
        Assert.AreEqual("run_not_parsed", (await service.VerifyAsync(TenantA, failedRun, Expected, Actor, CancellationToken.None)).RefusalReason);

        var good = await RetrievalAsync(db, TenantA, "good", Uri, Doc);
        var unattestedRun = (await new EfEvidenceParseService(db, TimeProvider.System).ParseAsync(TenantA, good, Actor, CancellationToken.None)).Run!.Id;
        Assert.AreEqual("no_attestation", (await service.VerifyAsync(TenantA, unattestedRun, Expected, Actor, CancellationToken.None)).RefusalReason);

        var unknown = await service.VerifyAsync(TenantA, Guid.NewGuid(), Expected, Actor, CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.RunNotFound, unknown.Outcome);
        Assert.AreEqual(0, await db.TenantEvidenceVerifications.CountAsync());
    }

    [TestMethod]
    public async Task AnotherTenantsRunIsNotFoundAndNeverVerified()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var (run, _) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated());
        var service = Service(db, new EchoVerifier());

        var cross = await service.VerifyAsync(TenantB, run, Expected, Actor, CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.RunNotFound, cross.Outcome);
        Assert.IsFalse(await service.IsVerifiedAsync(TenantB, run, CancellationToken.None));

        await service.VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None);
        Assert.IsTrue(await service.IsVerifiedAsync(TenantA, run, CancellationToken.None));
        Assert.IsFalse(await service.IsVerifiedAsync(TenantB, run, CancellationToken.None));
    }

    [TestMethod]
    public async Task TamperedChunksAreNotReproducibleFromTheRawBytes()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var (run, _) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated());
        // The chunk table is append-only for application paths; drop the trigger only to simulate corruption.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE tenant_evidence_chunks DISABLE TRIGGER tr_tenant_evidence_chunks_append_only");
        await db.Database.ExecuteSqlRawAsync("UPDATE tenant_evidence_chunks SET text = 'Altered text.' WHERE ordinal = 0");
        var result = await Service(db, new EchoVerifier()).VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None);
        Assert.AreEqual("parse_not_reproducible", result.RefusalReason);
        Assert.AreEqual(0, await db.TenantEvidenceVerifications.CountAsync());
    }

    [TestMethod]
    public async Task VerifiedRowsAreAppendOnlyAndTheDatabaseGuardsTheirShape()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var (run, attestation) = await SeedAsync(db, TenantA, "r1", Doc, Authenticated());
        var verified = (await Service(db, new EchoVerifier()).VerifyAsync(TenantA, run, Expected, Actor, CancellationToken.None)).Verification!;

        await AssertPgErrorAsync(db, "UPDATE tenant_evidence_verifications SET created_at_utc = now()", "55000");
        await AssertPgErrorAsync(db, "DELETE FROM tenant_evidence_verifications", "55000");
        await AssertPgErrorAsync(db, "TRUNCATE tenant_evidence_verifications", "55000");

        // Direct inserts bypassing the service: an unauthenticated attestation of another retrieval,
        // and a run that is not parsed_unverified, are refused by the database itself.
        var other = await RetrievalAsync(db, TenantA, "r2", Uri + "x", Doc);
        var otherRun = (await new EfEvidenceParseService(db, TimeProvider.System).ParseAsync(TenantA, other, Actor, CancellationToken.None)).Run!.Id;
        await AssertPgErrorAsync(db, Insert(otherRun, other, attestation, verified.ParentEvidenceRef), "23514");

        var bad = await RetrievalAsync(db, TenantA, "r3", Uri + "y", "<not-legislation/>");
        var failedRun = (await new EfEvidenceParseService(db, TimeProvider.System).ParseAsync(TenantA, bad, Actor, CancellationToken.None)).Run!.Id;
        var badAttestation = await AttestAsync(db, TenantA, bad, Authenticated() with { AttestationKey = "k-bad" });
        await AssertPgErrorAsync(db, Insert(failedRun, bad, badAttestation, verified.ParentEvidenceRef), "23514");

        // One verification per run.
        await AssertPgErrorAsync(db, Insert(run, verified.RawRetrievalId, attestation, verified.ParentEvidenceRef), "23505");
    }

    private static string Insert(Guid run, Guid retrieval, Guid attestation, string parentRef) =>
        $"INSERT INTO tenant_evidence_verifications (tenant_id, id, parse_run_id, raw_retrieval_id, attestation_id, parent_evidence_ref, created_at_utc) VALUES ('{TenantA}', '{Guid.NewGuid()}', '{run}', '{retrieval}', '{attestation}', '{parentRef}', now())";

    private static async Task AssertPgErrorAsync(VeritasAtlasDbContext db, string sql, string sqlState)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.AreEqual(sqlState, ex.SqlState);
    }

    private static async Task AssertNothingWrittenAsync(string cs, Guid run, EfEvidenceVerificationService service)
    {
        await using var check = new VeritasAtlasDbContext(Options(cs));
        Assert.AreEqual(0, await check.TenantEvidenceVerifications.CountAsync());
        Assert.AreEqual(0, await check.TenantAuditEvents.CountAsync(e => e.Action == EfEvidenceVerificationService.RecordedAction));
        Assert.IsFalse(await service.IsVerifiedAsync(TenantA, run, CancellationToken.None));
    }

    private static EfEvidenceVerificationService Service(VeritasAtlasDbContext db, IPublisherBindingVerifier verifier) =>
        new(db, verifier, new FixedTime(At.AddHours(2)));

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private static RetrievalAttestationInput Authenticated() => new(
        Guid.Empty, "k1", "synthetic-retriever", "synthetic-publisher", PublisherBindingMechanism.SourceRegistry,
        "registry-entry-1", SourceAuthenticationResult.Authenticated, "gb-uk", AttestedSourceType.Statute, At.AddMinutes(5));

    private static async Task<(Guid Run, Guid Attestation)> SeedAsync(
        VeritasAtlasDbContext db, Guid tenant, string key, string xml, RetrievalAttestationInput attestation)
    {
        var retrieval = await RetrievalAsync(db, tenant, key, Uri, xml);
        var run = (await new EfEvidenceParseService(db, TimeProvider.System).ParseAsync(tenant, retrieval, Actor, CancellationToken.None)).Run!.Id;
        return (run, await AttestAsync(db, tenant, retrieval, attestation));
    }

    private static async Task<Guid> AttestAsync(VeritasAtlasDbContext db, Guid tenant, Guid retrieval, RetrievalAttestationInput input)
    {
        var result = await new EfTenantRetrievalAttestationLedger(db, TimeProvider.System)
            .RecordAsync(tenant, input with { RawRetrievalId = retrieval }, "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, result.Outcome);
        return result.Attestation!.Id;
    }

    private static async Task<Guid> RetrievalAsync(VeritasAtlasDbContext db, Guid tenant, string key, string finalUri, string xml)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var result = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, finalUri, At, Encoding.UTF8.GetBytes(xml)),
            "synthetic-seeder", CancellationToken.None);
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
        builder.Database += "_evidenceverify";
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
