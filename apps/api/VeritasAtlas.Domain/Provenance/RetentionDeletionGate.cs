namespace VeritasAtlas.Domain.Provenance;

public enum RetentionRecordKind
{
    Unknown = 0,
    AuditLedger,
    Attestation,
    RawRetrieval,
    VerifiedEvidence,
    PublicationRevision,
    WorkspaceItem
}

public enum RetentionOutcome { Allow, Deny }

public static class RetentionDenials
{
    public const string OperationIdMissing = "operation_id_missing";
    public const string UnknownKind = "unknown_record_kind";
    public const string NotFound = "not_found";
    public const string Immutable = "immutable_record";
    public const string LegalHold = "legal_hold";
    public const string LiveReference = "live_reference";
    public const string AuditMissing = "audit_trail_missing";
    public const string RetentionNotElapsed = "retention_not_elapsed";
}

/// <summary>Server-supplied facts about one purge attempt. Tenant ids are compared ordinally; blanks fail closed.</summary>
public sealed record RetentionRequest(
    RetentionRecordKind Kind, string? RecordTenantId, string? RequestTenantId, string? OperationId,
    DateTimeOffset? RetainUntil, DateTimeOffset Now, bool LegalHold, bool HasLiveReference, bool AuditTrailPresent);

public sealed record RetentionDecision(RetentionOutcome Outcome, string? DenialCode);

/// <summary>
/// Pure deterministic, fail-closed purge eligibility for VER-P0-18 slice 1. Audit and attestation records and
/// anything that evidence or a publication still cites are never deletable. Tenant mismatch reports
/// <c>not_found</c> so the rule is not an existence oracle.
/// </summary>
public static class RetentionDeletionGate
{
    public static RetentionDecision Evaluate(RetentionRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (string.IsNullOrWhiteSpace(r.RecordTenantId) || string.IsNullOrWhiteSpace(r.RequestTenantId) ||
            !string.Equals(r.RecordTenantId, r.RequestTenantId, StringComparison.Ordinal)) return Deny(RetentionDenials.NotFound);
        if (string.IsNullOrWhiteSpace(r.OperationId)) return Deny(RetentionDenials.OperationIdMissing);
        if (!Enum.IsDefined(r.Kind) || r.Kind == RetentionRecordKind.Unknown) return Deny(RetentionDenials.UnknownKind);
        if (r.Kind is RetentionRecordKind.AuditLedger or RetentionRecordKind.Attestation or RetentionRecordKind.VerifiedEvidence
            or RetentionRecordKind.PublicationRevision) return Deny(RetentionDenials.Immutable);
        if (r.LegalHold) return Deny(RetentionDenials.LegalHold);
        if (r.HasLiveReference) return Deny(RetentionDenials.LiveReference);
        if (!r.AuditTrailPresent) return Deny(RetentionDenials.AuditMissing);
        if (r.RetainUntil is null || r.Now < r.RetainUntil.Value) return Deny(RetentionDenials.RetentionNotElapsed);
        return new(RetentionOutcome.Allow, null);
    }

    private static RetentionDecision Deny(string code) => new(RetentionOutcome.Deny, code);
}
