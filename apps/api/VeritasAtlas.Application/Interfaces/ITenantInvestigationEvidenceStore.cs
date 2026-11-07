namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-10 slice 2 (#210): collect verified evidence records into a tenant investigation.
/// Only ids of verified evidence (<c>tenant_evidence_verifications</c>) are accepted, so raw or
/// unverified retrievals can never be collected. Items are append-only; original sources are
/// never touched. Every read and write is scoped to the caller tenant, and another tenant's
/// investigation or evidence is indistinguishable from an unknown one.
/// </summary>
public interface ITenantInvestigationEvidenceStore
{
    Task<EvidenceItemCollectResult> CollectAsync(
        Guid tenantId, Guid investigationId, EvidenceItemInput input, CancellationToken cancellationToken);

    /// <summary>Null when the investigation does not exist in the tenant.</summary>
    Task<IReadOnlyList<TenantInvestigationEvidenceItemModel>?> ListAsync(
        Guid tenantId, Guid investigationId, CancellationToken cancellationToken);

    /// <summary>
    /// VER-P0-10 slice 3: read-only side-by-side comparison of two collected items. Null when the
    /// investigation or either item is not in the caller tenant's investigation.
    /// </summary>
    Task<TenantInvestigationEvidenceComparisonModel?> CompareAsync(
        Guid tenantId, Guid investigationId, Guid leftItemId, Guid rightItemId, CancellationToken cancellationToken);
}

public sealed record ComparedEvidenceModel(
    Guid ItemId,
    Guid EvidenceVerificationId,
    string? Note,
    string ParentEvidenceRef,
    Guid RawRetrievalId,
    Guid AttestationId,
    Guid ParseRunId,
    DateTime VerifiedAtUtc);

public sealed record TenantInvestigationEvidenceComparisonModel(
    ComparedEvidenceModel Left,
    ComparedEvidenceModel Right,
    bool SameEvidenceRef,
    bool SameRawRetrieval,
    bool SameAttestation,
    bool SameParseRun);

public sealed record EvidenceItemInput(Guid EvidenceVerificationId, string? Note, string CollectedBySubject);

public sealed record TenantInvestigationEvidenceItemModel(
    Guid Id,
    Guid InvestigationId,
    Guid EvidenceVerificationId,
    string? Note,
    string CollectedBySubject,
    DateTime CreatedAtUtc);

public enum EvidenceItemCollectOutcome
{
    Collected,

    /// <summary>The same evidence with the same note was already collected: the original row.</summary>
    Replayed,

    /// <summary>The evidence was already collected with a different note.</summary>
    NoteConflict,

    InvestigationNotFound,

    EvidenceNotFound
}

public sealed record EvidenceItemCollectResult(
    EvidenceItemCollectOutcome Outcome, TenantInvestigationEvidenceItemModel? Item);

public static class TenantInvestigationEvidenceLimits
{
    public const int MaxNoteLength = 500;

    public static bool IsValidNote(string? value) =>
        value is null
        || (!string.IsNullOrWhiteSpace(value)
            && value.Length <= MaxNoteLength
            && !char.IsWhiteSpace(value[0])
            && !char.IsWhiteSpace(value[^1])
            && !value.Any(char.IsControl));
}
