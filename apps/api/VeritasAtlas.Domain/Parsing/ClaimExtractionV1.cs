using System.Security.Cryptography;
using System.Text;

namespace VeritasAtlas.Domain.Parsing;

public enum ClaimStatus
{
    Unconfirmed,
    Confirmed
}

public enum ClaimExtractionFailure
{
    MissingTenant,
    InvalidChunk
}

/// <summary>Exact location of a claim quotation inside one parsed chunk.</summary>
public sealed record ClaimEvidenceSpan(
    string ParentEvidenceRef, string ElementId, string ChunkTextSha256, int Start, int Length);

/// <summary>
/// A candidate claim. <see cref="Quotation"/> is an exact substring of the chunk text at
/// <see cref="Span"/>. Extraction only ever produces <see cref="ClaimStatus.Unconfirmed"/>.
/// </summary>
public sealed record ExtractedClaim(
    Guid TenantId, string ClaimKey, string Quotation, ClaimEvidenceSpan Span, ClaimStatus Status);

public sealed record ClaimExtractionResult(
    bool Succeeded, ClaimExtractionFailure? Failure, string? Reason, IReadOnlyList<ExtractedClaim> Claims)
{
    public static ClaimExtractionResult Fail(ClaimExtractionFailure failure, string reason) =>
        new(false, failure, reason, Array.Empty<ExtractedClaim>());
}

/// <summary>
/// Seam for a later extraction method (for example a model-backed one). Implementations must not
/// bypass <see cref="ClaimExtractionV1.Verify"/>: a claim without a verifiable span is not a claim.
/// This slice contains no implementation that calls a model or any external service (#162 option A).
/// </summary>
public interface IClaimExtractor
{
    ClaimExtractionResult Extract(Guid tenantId, IReadOnlyList<EvidenceChunk> chunks);
}

/// <summary>
/// VER-P0-11 slice (#165): deterministic, rule-based claim extraction. A sentence of a chunk is a
/// candidate when it contains a normative word (shall, must, may not). Every claim carries the
/// chunk reference and exact character span, starts <see cref="ClaimStatus.Unconfirmed"/>, and is
/// never answerable until an analyst confirms it (<see cref="IsAnswerable"/>).
/// Fail closed: a chunk whose text does not match its digest fails the whole extraction.
/// </summary>
public sealed class ClaimExtractionV1 : IClaimExtractor
{
    public const string Name = "claim-extraction-rules";
    public const string Version = "1.0.0";
    public const int MaxClaimsPerChunk = 200;

    private static readonly string[] NormativePhrases =
        { " shall ", " must ", " may not ", " must not ", " shall not " };

    public ClaimExtractionResult Extract(Guid tenantId, IReadOnlyList<EvidenceChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (tenantId == Guid.Empty)
            return ClaimExtractionResult.Fail(ClaimExtractionFailure.MissingTenant, "missing_tenant");

        var claims = new List<ExtractedClaim>();
        foreach (var chunk in chunks)
        {
            if (chunk is null || string.IsNullOrWhiteSpace(chunk.ParentEvidenceRef)
                || string.IsNullOrWhiteSpace(chunk.ElementId) || string.IsNullOrEmpty(chunk.Text))
                return ClaimExtractionResult.Fail(ClaimExtractionFailure.InvalidChunk, "incomplete_chunk");
            if (!string.Equals(Sha(chunk.Text), chunk.TextSha256, StringComparison.Ordinal))
                return ClaimExtractionResult.Fail(ClaimExtractionFailure.InvalidChunk, "chunk_digest_mismatch");

            var perChunk = 0;
            foreach (var (start, length) in Sentences(chunk.Text))
            {
                var sentence = chunk.Text.Substring(start, length);
                if (!IsNormative(sentence)) continue;
                if (++perChunk > MaxClaimsPerChunk)
                    return ClaimExtractionResult.Fail(ClaimExtractionFailure.InvalidChunk, "too_many_claims");
                var span = new ClaimEvidenceSpan(
                    chunk.ParentEvidenceRef, chunk.ElementId, chunk.TextSha256, start, length);
                var key = Sha($"{tenantId:N}|{chunk.ParentEvidenceRef}|{chunk.ElementId}|{start}|{length}|{sentence}");
                claims.Add(new ExtractedClaim(tenantId, key, sentence, span, ClaimStatus.Unconfirmed));
            }
        }
        return new ClaimExtractionResult(true, null, null, claims);
    }

    /// <summary>True only if the claim quotation is exactly the cited span of a chunk of the same evidence.</summary>
    public static bool Verify(ExtractedClaim claim, EvidenceChunk chunk)
    {
        if (claim is null || chunk is null) return false;
        var s = claim.Span;
        return s.ParentEvidenceRef == chunk.ParentEvidenceRef
            && s.ElementId == chunk.ElementId
            && s.ChunkTextSha256 == chunk.TextSha256
            && Sha(chunk.Text) == chunk.TextSha256
            && s.Start >= 0 && s.Length > 0 && s.Start + s.Length <= chunk.Text.Length
            && string.Equals(chunk.Text.Substring(s.Start, s.Length), claim.Quotation, StringComparison.Ordinal);
    }

    /// <summary>Only a confirmed claim of the asking tenant whose evidence verifies may surface as an answer.</summary>
    public static bool IsAnswerable(ExtractedClaim claim, Guid askingTenantId, EvidenceChunk chunk) =>
        claim is not null && askingTenantId != Guid.Empty && claim.TenantId == askingTenantId
        && claim.Status == ClaimStatus.Confirmed && Verify(claim, chunk);

    /// <summary>The only transition: an explicit analyst confirmation; the quotation and span are unchanged.</summary>
    public static ExtractedClaim Confirm(ExtractedClaim claim) => claim with { Status = ClaimStatus.Confirmed };

    private static bool IsNormative(string sentence)
    {
        var padded = " " + sentence.ToLowerInvariant() + " ";
        foreach (var p in NormativePhrases)
            if (padded.Contains(p, StringComparison.Ordinal)) return true;
        return false;
    }

    private static IEnumerable<(int Start, int Length)> Sentences(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '.' && text[i] != ';') continue;
            var end = i + 1;
            if (end < text.Length && !char.IsWhiteSpace(text[end])) continue;
            if (end > start && text.Substring(start, end - start).Trim().Length > 0)
            {
                var lead = 0;
                while (char.IsWhiteSpace(text[start + lead])) lead++;
                yield return (start + lead, end - start - lead);
            }
            start = end;
        }
        if (start < text.Length)
        {
            var tail = text.Substring(start);
            if (tail.Trim().Length > 0)
            {
                var lead = 0;
                while (char.IsWhiteSpace(tail[lead])) lead++;
                var trimmed = tail.TrimEnd();
                yield return (start + lead, trimmed.Length - lead);
            }
        }
    }

    private static string Sha(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
