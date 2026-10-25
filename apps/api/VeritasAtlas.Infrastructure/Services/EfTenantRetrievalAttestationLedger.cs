using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// PostgreSQL-backed append-only retrieval attestation ledger (VER-P0-03 slice
/// 12, #107/#109). The database enforces the controlled vocabularies, the text
/// shapes, the same-tenant link to the raw retrieval, and refuses
/// UPDATE/DELETE/TRUNCATE; this service checks the same rules first so callers
/// get a typed outcome rather than a raw error. It records an attestation only:
/// it never verifies one and never writes an evidence reference.
/// </summary>
public sealed partial class EfTenantRetrievalAttestationLedger : ITenantRetrievalAttestationLedger
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    public const string RecordedAction = "tenant_retrieval_attestation.recorded";
    public const string AuditEntityType = "tenant_retrieval_attestation";
    private readonly VeritasAtlasDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfTenantRetrievalAttestationLedger(VeritasAtlasDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<RetrievalAttestationRecordResult> RecordAsync(
        Guid tenantId, RetrievalAttestationInput input, string actorSubject, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        Validate(input);
        // Same rule as the audit ledger's actor check
        // (ck_tenant_audit_events_actor_subject_shape).
        if (string.IsNullOrWhiteSpace(actorSubject)
            || actorSubject.Length > TenantAuditEventLimits.MaxSubjectLength
            || actorSubject.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid actor subject.", nameof(actorSubject));
        }

        var retrievalExists = await _db.TenantRawRetrievals.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == input.RawRetrievalId, cancellationToken);
        if (!retrievalExists)
        {
            return new RetrievalAttestationRecordResult(RetrievalAttestationRecordOutcome.RetrievalNotFound, null);
        }

        var row = new TenantRetrievalAttestation
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            RawRetrievalId = input.RawRetrievalId,
            AttestationKey = input.AttestationKey,
            RetrieverIdentity = input.RetrieverIdentity,
            PublisherIdentity = input.PublisherIdentity,
            BindingMechanism = ToDb(input.BindingMechanism),
            BindingReference = input.BindingReference,
            AuthenticationResult = ToDb(input.AuthenticationResult),
            Jurisdiction = input.Jurisdiction,
            SourceType = ToDb(input.SourceType),
            AttestedAtUtc = ToPostgresPrecision(input.AttestedAtUtc),
            CreatedAtUtc = ToPostgresPrecision(_timeProvider.GetUtcNow().UtcDateTime)
        };

        var existing = await FindByKeyAsync(tenantId, input.AttestationKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, row);
        }

        // Same SaveChanges, so one transaction: the attestation never exists
        // without its audit event, nor the event without it. The event carries
        // no identity, binding, result, jurisdiction, source type or retrieval id.
        var audit = new TenantAuditEvent
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            EventKey = $"{RecordedAction}:{row.Id:D}",
            Action = RecordedAction,
            EntityType = AuditEntityType,
            EntityId = row.Id.ToString("D"),
            ActorSubject = actorSubject,
            OccurredAtUtc = row.CreatedAtUtc
        };

        _db.TenantRetrievalAttestations.Add(row);
        _db.TenantAuditEvents.Add(audit);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation or ForeignKeyViolation } pg)
        {
            _db.Entry(row).State = EntityState.Detached;
            _db.Entry(audit).State = EntityState.Detached;
            if (pg.SqlState == ForeignKeyViolation)
            {
                return new RetrievalAttestationRecordResult(RetrievalAttestationRecordOutcome.RetrievalNotFound, null);
            }

            // A concurrent writer won the attestation key: replay against it.
            var winner = await FindByKeyAsync(tenantId, input.AttestationKey, cancellationToken)
                ?? throw new InvalidOperationException("Attestation key winner is not visible.", ex);
            return Replay(winner, row);
        }

        return new RetrievalAttestationRecordResult(RetrievalAttestationRecordOutcome.Recorded, ToModel(row));
    }

    public async Task<TenantRetrievalAttestationModel?> GetAsync(
        Guid tenantId, Guid attestationId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var row = await _db.TenantRetrievalAttestations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == attestationId, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async Task<IReadOnlyList<TenantRetrievalAttestationModel>> ListForRetrievalAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var rows = await _db.TenantRetrievalAttestations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RawRetrievalId == rawRetrievalId)
            .OrderBy(x => x.AttestedAtUtc).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    private Task<TenantRetrievalAttestation?> FindByKeyAsync(
        Guid tenantId, string attestationKey, CancellationToken cancellationToken) =>
        _db.TenantRetrievalAttestations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.AttestationKey == attestationKey, cancellationToken);

    private static RetrievalAttestationRecordResult Replay(
        TenantRetrievalAttestation existing, TenantRetrievalAttestation candidate)
    {
        var same = existing.RawRetrievalId == candidate.RawRetrievalId
            && existing.RetrieverIdentity == candidate.RetrieverIdentity
            && existing.PublisherIdentity == candidate.PublisherIdentity
            && existing.BindingMechanism == candidate.BindingMechanism
            && existing.BindingReference == candidate.BindingReference
            && existing.AuthenticationResult == candidate.AuthenticationResult
            && existing.Jurisdiction == candidate.Jurisdiction
            && existing.SourceType == candidate.SourceType
            && existing.AttestedAtUtc == candidate.AttestedAtUtc;
        return same
            ? new RetrievalAttestationRecordResult(RetrievalAttestationRecordOutcome.Replayed, ToModel(existing))
            : new RetrievalAttestationRecordResult(RetrievalAttestationRecordOutcome.IdempotencyConflict, null);
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
    }

    private static void Validate(RetrievalAttestationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RawRetrievalId == Guid.Empty)
        {
            throw new ArgumentException("A raw retrieval is required.", nameof(input));
        }

        // Same exact-text rule as ck_tenant_retrieval_attestations_attestation_key_shape.
        if (!IsExactText(input.AttestationKey, TenantRetrievalAttestationLimits.MaxAttestationKeyLength))
        {
            throw new ArgumentException("Invalid attestation key.", nameof(input));
        }

        if (!IsExactText(input.RetrieverIdentity, TenantRetrievalAttestationLimits.MaxIdentityLength)
            || !IsExactText(input.PublisherIdentity, TenantRetrievalAttestationLimits.MaxIdentityLength)
            || !IsExactText(input.BindingReference, TenantRetrievalAttestationLimits.MaxBindingReferenceLength))
        {
            throw new ArgumentException("Invalid retriever, publisher or binding reference.", nameof(input));
        }

        if (input.Jurisdiction is null
            || input.Jurisdiction.Length > TenantRetrievalAttestationLimits.MaxJurisdictionLength
            || !JurisdictionPattern().IsMatch(input.Jurisdiction))
        {
            throw new ArgumentException("Invalid jurisdiction.", nameof(input));
        }

        if (input.AttestedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Attestation time must be UTC.", nameof(input));
        }

        if (!Enum.IsDefined(input.BindingMechanism)
            || !Enum.IsDefined(input.AuthenticationResult)
            || !Enum.IsDefined(input.SourceType))
        {
            throw new ArgumentException("Unknown binding mechanism, authentication result or source type.", nameof(input));
        }
    }

    // Stricter than the *_shape checks: refuses any Unicode leading/trailing
    // whitespace, and char.IsControl covers C0, DEL and C1.
    private static bool IsExactText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maxLength
        && !char.IsWhiteSpace(value[0])
        && !char.IsWhiteSpace(value[^1])
        && !value.Any(char.IsControl);

    // Same pattern as ck_tenant_retrieval_attestations_jurisdiction_code; \z
    // refuses a trailing newline that $ would accept.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex JurisdictionPattern();

    private static string ToDb(PublisherBindingMechanism value) => value switch
    {
        PublisherBindingMechanism.SourceRegistry => "source_registry",
        PublisherBindingMechanism.PublisherSignature => "publisher_signature",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToDb(SourceAuthenticationResult value) => value switch
    {
        SourceAuthenticationResult.Authenticated => "authenticated",
        SourceAuthenticationResult.Unauthenticated => "unauthenticated",
        SourceAuthenticationResult.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToDb(AttestedSourceType value) => value switch
    {
        AttestedSourceType.CourtRecord => "court_record",
        AttestedSourceType.Statute => "statute",
        AttestedSourceType.Regulation => "regulation",
        AttestedSourceType.Register => "register",
        AttestedSourceType.Filing => "filing",
        AttestedSourceType.OtherPublicRecord => "other_public_record",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static PublisherBindingMechanism MechanismFromDb(string value) => value switch
    {
        "source_registry" => PublisherBindingMechanism.SourceRegistry,
        "publisher_signature" => PublisherBindingMechanism.PublisherSignature,
        _ => throw new InvalidOperationException("Unknown stored binding mechanism.")
    };

    private static SourceAuthenticationResult ResultFromDb(string value) => value switch
    {
        "authenticated" => SourceAuthenticationResult.Authenticated,
        "unauthenticated" => SourceAuthenticationResult.Unauthenticated,
        "failed" => SourceAuthenticationResult.Failed,
        _ => throw new InvalidOperationException("Unknown stored authentication result.")
    };

    private static AttestedSourceType SourceTypeFromDb(string value) => value switch
    {
        "court_record" => AttestedSourceType.CourtRecord,
        "statute" => AttestedSourceType.Statute,
        "regulation" => AttestedSourceType.Regulation,
        "register" => AttestedSourceType.Register,
        "filing" => AttestedSourceType.Filing,
        "other_public_record" => AttestedSourceType.OtherPublicRecord,
        _ => throw new InvalidOperationException("Unknown stored source type.")
    };

    // PostgreSQL stores microseconds: round-trip-stable values keep a recorded
    // result identical to a later read or replay of the same row.
    private static DateTime ToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static TenantRetrievalAttestationModel ToModel(TenantRetrievalAttestation row) =>
        new(row.Id, row.RawRetrievalId, row.AttestationKey, row.RetrieverIdentity, row.PublisherIdentity,
            MechanismFromDb(row.BindingMechanism), row.BindingReference,
            ResultFromDb(row.AuthenticationResult), row.Jurisdiction, SourceTypeFromDb(row.SourceType),
            DateTime.SpecifyKind(row.AttestedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
}
