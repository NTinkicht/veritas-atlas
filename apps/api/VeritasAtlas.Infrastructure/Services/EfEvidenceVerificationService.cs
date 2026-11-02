using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Parsing;
using VeritasAtlas.Domain.Provenance;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-07 slice 3 (#174): writes the append-only "verified and answerable" row for a parse
/// run, together with its audit event, in one SaveChanges. Every check is fail-closed and the
/// first refusal wins: <c>run_not_parsed</c>, <c>no_chunks</c>, <c>retrieval_not_found</c>,
/// <c>parse_not_reproducible</c> (the chunks do not re-derive from the stored raw bytes),
/// <c>no_attestation</c>, then the attestation binding reason (including
/// <c>publisher_binding_unverified</c> when the trusted verifier does not confirm), then the
/// raw-retrieval eligibility reason. The publisher binding is only ever confirmed by the
/// injected <see cref="IPublisherBindingVerifier"/>; a stored attestation alone never verifies.
/// </summary>
public sealed class EfEvidenceVerificationService : IEvidenceVerificationService
{
    public const string RecordedAction = "tenant_evidence_verification.recorded";
    public const string AuditEntityType = "tenant_evidence_verification";
    private const string UniqueViolation = "23505";
    private readonly VeritasAtlasDbContext _db;
    private readonly IPublisherBindingVerifier _verifier;
    private readonly TimeProvider _timeProvider;

    public EfEvidenceVerificationService(
        VeritasAtlasDbContext db, IPublisherBindingVerifier verifier, TimeProvider timeProvider)
    {
        _db = db;
        _verifier = verifier;
        _timeProvider = timeProvider;
    }

    public async Task<EvidenceVerificationResult> VerifyAsync(
        Guid tenantId, Guid parseRunId, EvidenceClassificationExpectation expectation,
        string actorSubject, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (parseRunId == Guid.Empty) throw new ArgumentException("A parse run is required.", nameof(parseRunId));
        ArgumentNullException.ThrowIfNull(expectation);
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
            throw new ArgumentException("Invalid actor subject.", nameof(actorSubject));

        var run = await _db.TenantParseRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == parseRunId, cancellationToken);
        if (run is null) return new(EvidenceVerificationOutcome.RunNotFound, null, null);

        var existing = await FindAsync(tenantId, parseRunId, cancellationToken);
        if (existing is not null) return new(EvidenceVerificationOutcome.Replayed, ToModel(existing), null);

        if (run.Outcome != "parsed_unverified") return Refuse("run_not_parsed");
        var chunks = await _db.TenantEvidenceChunks.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ParseRunId == parseRunId)
            .ToListAsync(cancellationToken);
        if (chunks.Count == 0) return Refuse("no_chunks");
        var parentRef = chunks[0].ParentEvidenceRef;
        if (chunks.Any(c => c.ParentEvidenceRef != parentRef)) return Refuse("parse_not_reproducible");

        var retrieval = await _db.TenantRawRetrievals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == run.RawRetrievalId, cancellationToken);
        if (retrieval is null) return Refuse("retrieval_not_found");
        if (!Reproduces(retrieval, run, chunks, parentRef)) return Refuse("parse_not_reproducible");

        var attestations = await _db.TenantRetrievalAttestations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RawRetrievalId == retrieval.Id)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (attestations.Count == 0) return Refuse("no_attestation");

        var now = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime);
        var expected = new AttestationExpectation(expectation.Jurisdiction, expectation.SourceType, now);
        var attested = new AttestedRetrieval(
            retrieval.ContentSha256, retrieval.FinalUri, UtcKind(retrieval.RetrievedAtUtc));
        TenantRetrievalAttestation? bound = null;
        string? firstReason = null;
        foreach (var attestation in attestations)
        {
            var claim = ToClaim(attestation, retrieval);
            PublisherBindingVerification verification;
            try
            {
                verification = await _verifier.VerifyAsync(claim, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failing trusted boundary fails closed; its error text is not kept.
                verification = new PublisherBindingVerification(null, null, null, null, null, false);
            }

            var assessment = RawRetrievalProvenanceComposer.Compose(
                retrieval.RawBytes, attested, claim, expected, verification);
            var state = assessment.State with
            {
                ParserName = run.ParserName,
                ParserVersion = run.ParserVersion,
                ParserVerified = true,
                EvidenceRef = parentRef
            };
            var eligibility = RawRetrievalEligibility.Evaluate(state);
            if (assessment.Reason is null && eligibility.Eligible)
            {
                bound = attestation;
                break;
            }

            firstReason ??= assessment.Reason ?? eligibility.Reason;
        }

        if (bound is null) return Refuse(firstReason ?? "no_attestation");

        var row = new TenantEvidenceVerification
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            ParseRunId = parseRunId,
            RawRetrievalId = retrieval.Id,
            AttestationId = bound.Id,
            ParentEvidenceRef = parentRef,
            CreatedAtUtc = now
        };
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{RecordedAction}:{row.Id:D}",
            Action = RecordedAction,
            EntityType = AuditEntityType,
            EntityId = row.Id.ToString("D"),
            ActorSubject = actorSubject,
            OccurredAtUtc = now
        };
        _db.TenantEvidenceVerifications.Add(row);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            var winner = await FindAsync(tenantId, parseRunId, cancellationToken)
                ?? throw new InvalidOperationException("Verification winner is not visible.", ex);
            return new(EvidenceVerificationOutcome.Replayed, ToModel(winner), null);
        }

        return new(EvidenceVerificationOutcome.Verified, ToModel(row), null);
    }

    public Task<bool> IsVerifiedAsync(Guid tenantId, Guid parseRunId, CancellationToken cancellationToken) =>
        tenantId == Guid.Empty || parseRunId == Guid.Empty
            ? Task.FromResult(false)
            : _db.TenantEvidenceVerifications.AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.ParseRunId == parseRunId, cancellationToken);

    private static EvidenceVerificationResult Refuse(string reason) =>
        new(EvidenceVerificationOutcome.Refused, null, reason);

    /// <summary>The stored chunks must be exactly what the parser derives from the stored bytes.</summary>
    private static bool Reproduces(
        TenantRawRetrieval retrieval, TenantParseRun run, List<TenantEvidenceChunk> chunks, string parentRef)
    {
        if (run.ParserName != UkLegislationParserV1.Name || run.ParserVersion != UkLegislationParserV1.Version)
            return false;
        if (!Uri.TryCreate(retrieval.FinalUri, UriKind.Absolute, out var uri)
            || uri.AbsolutePath.Trim('/').Length == 0)
            return false;
        try
        {
            var output = UkLegislationParserV1.Parse(
                retrieval.RawBytes, retrieval.FinalUri, uri.AbsolutePath.Trim('/'), retrieval.ContentSha256);
            if (!output.Succeeded || output.ParentEvidenceRef != parentRef
                || output.Chunks.Count != chunks.Count)
                return false;
            var stored = chunks.OrderBy(c => c.Ordinal).ToList();
            for (var i = 0; i < stored.Count; i++)
            {
                var c = output.Chunks[i];
                if (stored[i].Ordinal != c.Ordinal || stored[i].ElementId != c.ElementId
                    || stored[i].Text != c.Text || stored[i].TextSha256 != c.TextSha256)
                    return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static AttestationClaim ToClaim(TenantRetrievalAttestation a, TenantRawRetrieval r) => new(
        a.RetrieverIdentity, a.PublisherIdentity, a.BindingMechanism, a.BindingReference,
        a.AuthenticationResult, r.ContentSha256, r.FinalUri, a.Jurisdiction, a.SourceType,
        UtcKind(r.RetrievedAtUtc), UtcKind(a.AttestedAtUtc));

    private Task<TenantEvidenceVerification?> FindAsync(Guid tenantId, Guid parseRunId, CancellationToken ct) =>
        _db.TenantEvidenceVerifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ParseRunId == parseRunId, ct);

    private static DateTime UtcKind(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static EvidenceVerificationModel ToModel(TenantEvidenceVerification v) => new(
        v.Id, v.ParseRunId, v.RawRetrievalId, v.AttestationId, v.ParentEvidenceRef, UtcKind(v.CreatedAtUtc));
}
