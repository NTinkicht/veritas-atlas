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
/// VER-P0-08 (#175): tenant-scoped freshness of verified evidence. Database tests run on a real
/// PostgreSQL when <c>VERITAS_TEST_POSTGRES</c> is set (own <c>*_evidencefresh</c> database).
/// All fixtures are hand-written and SYNTHETIC.
/// </summary>
[TestClass]
public sealed class EvidenceFreshnessServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly DateTime At = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Uri = "https://www.legislation.gov.uk/synthetic/act/1/data.xml";
    private const string Doc = "<Legislation SchemaVersion=\"1\"><P1 id=\"s1\">Section one.</P1></Legislation>";
    private static readonly SourceFreshnessPolicy Policy =
        new("p1", "v1", "synthetic", true, 3600, "etag", false, "registry");

    private sealed class FixedPolicies(SourceFreshnessPolicy? policy) : ISourceFreshnessPolicyResolver
    {
        public Task<SourceFreshnessPolicyResolution?> ResolveAsync(Guid t, Guid s, CancellationToken ct) =>
            Task.FromResult<SourceFreshnessPolicyResolution?>(policy is null ? null : new(policy, policy));
    }

    private sealed class FixedRevisions(SourceRevisionEvidence evidence) : ISourceRevisionEvidenceProvider
    {
        public Task<SourceRevisionEvidence> GetAsync(Guid t, Guid r, CancellationToken ct) => Task.FromResult(evidence);
    }

    private sealed class EchoVerifier : IPublisherBindingVerifier
    {
        public Task<PublisherBindingVerification> VerifyAsync(AttestationClaim c, CancellationToken ct) =>
            Task.FromResult(new PublisherBindingVerification(
                c.PublisherIdentity, c.BindingMechanism, c.BindingReference, c.FinalUri, c.ContentSha256, true));
    }

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private static readonly SourceRevisionEvidence NoProof = new(false, false, false);

    [TestMethod]
    public async Task FailClosedProductionBoundariesProveNothing()
    {
        Assert.IsNull(await new FailClosedSourceFreshnessPolicyResolver().ResolveAsync(TenantA, Guid.NewGuid(), CancellationToken.None));
        var revision = await new FailClosedSourceRevisionEvidenceProvider().GetAsync(TenantA, Guid.NewGuid(), CancellationToken.None);
        Assert.AreEqual(NoProof, revision);
    }

    [TestMethod]
    public async Task InvalidInputIsRefusedBeforeTheDatabaseIsTouched()
    {
        var service = new EfEvidenceFreshnessService(null!, new FixedPolicies(Policy), new FixedRevisions(NoProof), TimeProvider.System);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.EvaluateAsync(Guid.Empty, "ev1:x", null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.EvaluateAsync(TenantA, " ", null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.EvaluateAsync(TenantA, "ev1:a\nb", null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.EvaluateAsync(TenantA, new string('x', 513), null, CancellationToken.None));
    }

    [TestMethod]
    public async Task VerifiedFreshEvidenceIsCurrentAndUsesTheServerClock()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var evidenceRef = await SeedVerifiedAsync(db, TenantA, "r1");

        var result = await Service(db, Policy, NoProof, At.AddMinutes(30)).EvaluateAsync(TenantA, evidenceRef, null, CancellationToken.None);
        Assert.AreEqual(EvidenceFreshnessOutcome.Evaluated, result.Outcome);
        Assert.AreEqual(FreshnessState.Current, result.Decision!.State);
        Assert.IsTrue(result.Decision.Eligible);
        Assert.AreEqual(1800, result.Decision.RetrievalAgeSeconds);
        Assert.AreEqual(SourceFreshnessEvaluator.Canonicalize(new DateTimeOffset(At.AddMinutes(30))), result.Decision.EvaluatedAt);
    }

    [TestMethod]
    public async Task OlderThanThePolicyWindowIsStaleAndIneligible()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var evidenceRef = await SeedVerifiedAsync(db, TenantA, "r1");

        var result = await Service(db, Policy, NoProof, At.AddHours(2)).EvaluateAsync(TenantA, evidenceRef, null, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Stale, result.Decision!.State);
        Assert.IsFalse(result.Decision.Eligible);
        Assert.AreEqual(SourceFreshnessReasons.FreshnessRequiredNonCurrent, result.Decision.Reason);
    }

    [TestMethod]
    public async Task MissingPolicyFailsClosedToUnknown()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var evidenceRef = await SeedVerifiedAsync(db, TenantA, "r1");

        var service = new EfEvidenceFreshnessService(
            db, new FailClosedSourceFreshnessPolicyResolver(), new FailClosedSourceRevisionEvidenceProvider(), new FixedTime(At.AddMinutes(1)));
        var result = await service.EvaluateAsync(TenantA, evidenceRef, null, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Unknown, result.Decision!.State);
        Assert.IsFalse(result.Decision.Eligible);
    }

    [TestMethod]
    public async Task ProvenReplacementIsSupersededAndUnprovenReplacementIsNot()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var evidenceRef = await SeedVerifiedAsync(db, TenantA, "r1");

        var proven = await Service(db, Policy, new(true, true, true), At.AddMinutes(1)).EvaluateAsync(TenantA, evidenceRef, null, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Superseded, proven.Decision!.State);
        Assert.IsFalse(proven.Decision.Eligible);

        var unproven = await Service(db, Policy, new(true, true, false), At.AddMinutes(1)).EvaluateAsync(TenantA, evidenceRef, null, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Current, unproven.Decision!.State);
    }

    [TestMethod]
    public async Task FreshnessFollowsTheNewestVerifiedRetrievalOfIdenticalContent()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var first = await SeedVerifiedAsync(db, TenantA, "r1");
        var second = await SeedVerifiedAsync(db, TenantA, "r2", At.AddHours(1));
        Assert.AreEqual(first, second);

        var result = await Service(db, Policy, NoProof, At.AddMinutes(90)).EvaluateAsync(TenantA, first, null, CancellationToken.None);
        Assert.AreEqual(FreshnessState.Current, result.Decision!.State);
        Assert.AreEqual(1800, result.Decision.RetrievalAgeSeconds);
    }

    [TestMethod]
    public async Task UnverifiedUnknownAndCrossTenantReferencesAreIndistinguishablyNotFound()
    {
        if (Database() is not { } cs) return;
        await ResetAsync(cs);
        await using var db = new VeritasAtlasDbContext(Options(cs));
        var verifiedRef = await SeedVerifiedAsync(db, TenantA, "r1");
        var unverifiedRef = await SeedParsedOnlyAsync(db, TenantA, "r2");
        var service = Service(db, Policy, NoProof, At.AddMinutes(1));

        foreach (var (tenant, reference) in new[] { (TenantB, verifiedRef), (TenantA, unverifiedRef), (TenantA, "ev1:does-not-exist") })
        {
            var result = await service.EvaluateAsync(tenant, reference, null, CancellationToken.None);
            Assert.AreEqual(EvidenceFreshnessOutcome.NotFound, result.Outcome);
            Assert.IsNull(result.Decision);
        }
    }

    private static EfEvidenceFreshnessService Service(
        VeritasAtlasDbContext db, SourceFreshnessPolicy? policy, SourceRevisionEvidence revision, DateTime now) =>
        new(db, new FixedPolicies(policy), new FixedRevisions(revision), new FixedTime(now));

    private static async Task<string> SeedParsedOnlyAsync(VeritasAtlasDbContext db, Guid tenant, string key)
    {
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var recorded = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, "https://www.legislation.gov.uk/synthetic/act/2/data.xml", At, Encoding.UTF8.GetBytes(Doc.Replace("one", "two"))),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, recorded.Outcome);
        var parsed = await new EfEvidenceParseService(db, TimeProvider.System)
            .ParseAsync(tenant, recorded.Record!.Id, "synthetic-seeder", CancellationToken.None);
        Assert.IsNotNull(parsed.Run);
        return await db.TenantEvidenceChunks.AsNoTracking()
            .Where(c => c.TenantId == tenant && c.ParseRunId == parsed.Run!.Id)
            .Select(c => c.ParentEvidenceRef).FirstAsync();
    }

    private static async Task<string> SeedVerifiedAsync(VeritasAtlasDbContext db, Guid tenant, string key, DateTime? retrievedAt = null)
    {
        var at = retrievedAt ?? At;
        var source = await new EfTenantSourceRegistry(db, TimeProvider.System)
            .CreateAsync(tenant, "https://example.test/" + key, "synthetic-seeder", CancellationToken.None);
        var recorded = await new EfTenantRawRetrievalLedger(db, TimeProvider.System).RecordAsync(
            tenant, new RawRetrievalInput(source!.Id, key, Uri, at, Encoding.UTF8.GetBytes(Doc)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RawRetrievalRecordOutcome.Recorded, recorded.Outcome);
        var retrieval = recorded.Record!.Id;
        var run = (await new EfEvidenceParseService(db, TimeProvider.System)
            .ParseAsync(tenant, retrieval, "synthetic-seeder", CancellationToken.None)).Run!.Id;
        var attested = await new EfTenantRetrievalAttestationLedger(db, TimeProvider.System).RecordAsync(
            tenant,
            new RetrievalAttestationInput(
                retrieval, "k-" + key, "synthetic-retriever", "synthetic-publisher", PublisherBindingMechanism.SourceRegistry,
                "registry-entry-1", SourceAuthenticationResult.Authenticated, "gb-uk", AttestedSourceType.Statute, at.AddMinutes(5)),
            "synthetic-seeder", CancellationToken.None);
        Assert.AreEqual(RetrievalAttestationRecordOutcome.Recorded, attested.Outcome);
        var verify = await new EfEvidenceVerificationService(db, new EchoVerifier(), new FixedTime(At.AddHours(2)))
            .VerifyAsync(tenant, run, new EvidenceClassificationExpectation("gb-uk", "statute"), "synthetic-verifier", CancellationToken.None);
        Assert.AreEqual(EvidenceVerificationOutcome.Verified, verify.Outcome);
        return verify.Verification!.ParentEvidenceRef;
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
        builder.Database += "_evidencefresh";
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
