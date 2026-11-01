using System.Text.RegularExpressions;

namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// What is known about one stored raw retrieval, supplied explicitly by the trusted boundaries
/// (VER-P0-06 slice 2, #129). <paramref name="ParserVerified"/> is true only after the versioned
/// parser (VER-P0-07) has run and its provenance checks passed; nothing in the retrieval path
/// can set it. <paramref name="EvidenceRef"/> is whatever <c>ev1:</c> value is attached, if any.
/// </summary>
public sealed record RawRetrievalProvenanceState(
    string? ContentSha256,
    string? FinalUri,
    bool RetrievalAttested,
    bool ClassificationProven,
    string? ParserName,
    string? ParserVersion,
    bool ParserVerified,
    string? EvidenceRef);

public sealed record RawRetrievalEligibilityDecision(bool Eligible, string? Reason)
{
    public static readonly RawRetrievalEligibilityDecision Allow = new(true, null);
    public static RawRetrievalEligibilityDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Pure, deterministic, fail-closed rule for when a stored raw retrieval may be cited or
/// answered from. Hashing and storing bytes never makes a record eligible: the versioned parser
/// must have verified it and a well-formed <c>ev1:</c> reference must be attached. This does not
/// derive or recompute the reference (that is <see cref="EvidenceProvenance"/>); it only refuses
/// to treat a record as answerable before every check has held. The first failing rule wins, in
/// this fixed order: <c>invalid_state</c>, <c>missing_content_digest</c>, <c>invalid_sha256</c>,
/// <c>missing_final_uri</c>, <c>unverified_evidence_ref_present</c>, <c>parser_unverified</c>,
/// <c>retrieval_unattested</c>, <c>classification_unproven</c>, <c>missing_evidence_ref</c>,
/// <c>invalid_evidence_ref</c>.
/// </summary>
public static class RawRetrievalEligibility
{
    private static readonly Regex Sha256Lower = new(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex EvidenceRefPattern = new(@"^ev1:[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

    public static RawRetrievalEligibilityDecision Evaluate(RawRetrievalProvenanceState? state)
    {
        if (state is null) return RawRetrievalEligibilityDecision.Deny("invalid_state");
        if (string.IsNullOrWhiteSpace(state.ContentSha256))
            return RawRetrievalEligibilityDecision.Deny("missing_content_digest");
        if (!Sha256Lower.IsMatch(state.ContentSha256))
            return RawRetrievalEligibilityDecision.Deny("invalid_sha256");
        if (string.IsNullOrWhiteSpace(state.FinalUri))
            return RawRetrievalEligibilityDecision.Deny("missing_final_uri");

        var parserRan = state.ParserVerified
            && !string.IsNullOrWhiteSpace(state.ParserName)
            && !string.IsNullOrWhiteSpace(state.ParserVersion);
        if (!parserRan && !string.IsNullOrWhiteSpace(state.EvidenceRef))
            return RawRetrievalEligibilityDecision.Deny("unverified_evidence_ref_present");
        if (!parserRan) return RawRetrievalEligibilityDecision.Deny("parser_unverified");
        if (!state.RetrievalAttested) return RawRetrievalEligibilityDecision.Deny("retrieval_unattested");
        if (!state.ClassificationProven) return RawRetrievalEligibilityDecision.Deny("classification_unproven");
        if (string.IsNullOrWhiteSpace(state.EvidenceRef))
            return RawRetrievalEligibilityDecision.Deny("missing_evidence_ref");
        if (!EvidenceRefPattern.IsMatch(state.EvidenceRef))
            return RawRetrievalEligibilityDecision.Deny("invalid_evidence_ref");
        return RawRetrievalEligibilityDecision.Allow;
    }
}
