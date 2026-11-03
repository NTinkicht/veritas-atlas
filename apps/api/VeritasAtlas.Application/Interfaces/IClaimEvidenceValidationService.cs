using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>Stable failure codes of <c>citation-integrity/v1</c>; no code carries a source payload.</summary>
public static class ClaimEvidenceCodes
{
    public const string NotFound = "EVIDENCE_NOT_FOUND";
    public const string DigestMismatch = "EVIDENCE_DIGEST_MISMATCH";
    public const string ParserMismatch = "EVIDENCE_REFERENCE_PARSER_MISMATCH";
    public const string Required = "EVIDENCE_REQUIRED";
    public const string ProvenanceIncomplete = "PROVENANCE_INCOMPLETE";
    public const string ScopeMismatch = "EVIDENCE_SCOPE_MISMATCH";
    public const string Stale = "EVIDENCE_STALE";
    public const string FreshnessUnknown = "EVIDENCE_FRESHNESS_UNKNOWN";
}

/// <summary>
/// One evidence item a claim cites: the immutable <c>ev1:</c> reference plus the parser name/version
/// the citation was minted under. <see cref="ScopeSourceEntryId"/> optionally pins the source the
/// claim is permitted to cite.
/// </summary>
public sealed record ClaimEvidenceCitation(
    string? EvidenceRef, string? ParserName, string? ParserVersion, Guid? ScopeSourceEntryId);

/// <summary>
/// A claim's evidence request. <see cref="EvidenceRequired"/> is the answer policy's classification
/// of the assertion; <see cref="Policy"/> is the claim-level freshness/historical policy.
/// </summary>
public sealed record ClaimEvidenceValidationRequest(
    bool EvidenceRequired, SourceFreshnessClaimPolicy? Policy, IReadOnlyList<ClaimEvidenceCitation>? Citations);

public sealed record ClaimEvidenceItemResult(
    string EvidenceRef, bool Valid, string? Code, FreshnessState? Freshness, string? EvaluatedAt, string? HistoricalAsOf);

public enum ClaimEvidenceValidationOutcome { Validated, InvalidRequest }

/// <summary><see cref="Valid"/> is true only when every item validated; <see cref="Code"/> is the first failure.</summary>
public sealed record ClaimEvidenceValidationResult(
    ClaimEvidenceValidationOutcome Outcome, bool Valid, string? Code, IReadOnlyList<ClaimEvidenceItemResult> Items);

public static class ClaimEvidenceLimits
{
    public const int MaxCitations = 20;
    public const int MaxTextLength = 128;
}

/// <summary>
/// VER-P0-12 (#182) slice 1: deterministic, fail-closed claim-to-evidence validation. Never repairs,
/// refreshes, reparses or substitutes evidence, and never reveals whether a reference exists in
/// another tenant (such a reference is indistinguishable from an unknown one).
/// </summary>
public interface IClaimEvidenceValidationService
{
    Task<ClaimEvidenceValidationResult> ValidateAsync(
        Guid tenantId, ClaimEvidenceValidationRequest request, CancellationToken cancellationToken);
}
