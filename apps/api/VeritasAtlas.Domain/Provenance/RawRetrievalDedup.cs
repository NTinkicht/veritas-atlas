using System.Text.RegularExpressions;

namespace VeritasAtlas.Domain.Provenance;

public enum RawRetrievalDedupAction
{
    /// <summary>No earlier retrieval exists for this source and final URI: store the bytes.</summary>
    StoreNew,

    /// <summary>The bytes equal the latest stored retrieval: write no new raw row.</summary>
    Duplicate,

    /// <summary>The bytes differ from the latest stored retrieval: store a new immutable row.</summary>
    ContentChanged,

    /// <summary>A digest was malformed; nothing may be stored or skipped on its basis.</summary>
    Refused
}

public sealed record RawRetrievalDedupDecision(RawRetrievalDedupAction Action, string? Reason)
{
    public static readonly RawRetrievalDedupDecision StoreNew = new(RawRetrievalDedupAction.StoreNew, null);
    public static readonly RawRetrievalDedupDecision Duplicate = new(RawRetrievalDedupAction.Duplicate, null);
    public static readonly RawRetrievalDedupDecision ContentChanged = new(RawRetrievalDedupAction.ContentChanged, null);
    public static RawRetrievalDedupDecision Refuse(string reason) => new(RawRetrievalDedupAction.Refused, reason);
}

/// <summary>
/// VER-P0-06 slice 3 (#131): pure, deterministic, fail-closed rule for whether a re-fetched body
/// is a duplicate of the latest stored raw retrieval for the same tenant source and final URI.
/// The caller supplies digests it computed server-side; digests must be exactly 64 lowercase hex
/// characters and are never normalised (a case-folded or padded digest is refused, so a mangled
/// value can never cause a stored row to be skipped). The new digest is checked first, then the
/// prior one: <c>invalid_new_digest</c>, <c>invalid_prior_digest</c>. A <c>Duplicate</c> means
/// "write no new raw row"; the existing immutable row is never altered by any outcome. This does
/// not touch the ledger, mint an <c>ev1:</c> reference, or make anything answerable.
/// </summary>
public static class RawRetrievalDedup
{
    private static readonly Regex Sha256Lower = new(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

    public static RawRetrievalDedupDecision Decide(string? latestStoredDigest, string? newDigest)
    {
        if (newDigest is null || !Sha256Lower.IsMatch(newDigest))
            return RawRetrievalDedupDecision.Refuse("invalid_new_digest");
        if (latestStoredDigest is null) return RawRetrievalDedupDecision.StoreNew;
        if (!Sha256Lower.IsMatch(latestStoredDigest))
            return RawRetrievalDedupDecision.Refuse("invalid_prior_digest");
        return string.Equals(latestStoredDigest, newDigest, StringComparison.Ordinal)
            ? RawRetrievalDedupDecision.Duplicate
            : RawRetrievalDedupDecision.ContentChanged;
    }
}
