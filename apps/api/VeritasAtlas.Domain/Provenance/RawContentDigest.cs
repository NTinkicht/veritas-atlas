using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VeritasAtlas.Domain.Provenance;

public sealed record RawContentDigestDecision(bool Matches, string? Reason)
{
    public static readonly RawContentDigestDecision Match = new(true, null);
    public static RawContentDigestDecision Refuse(string reason) => new(false, reason);
}

/// <summary>
/// VER-P0-06 slice 4: the single definition of the raw <c>content_sha256</c> digest. It is the
/// SHA-256 of the exact retrieved bytes (never of decoded or re-encoded text), rendered as exactly
/// 64 lowercase hex characters. <see cref="Verify"/> never normalises the claimed digest: anything
/// that is not exactly 64 lowercase hex characters is <c>invalid_claimed_digest</c>, and a
/// well-formed digest that differs is <c>digest_mismatch</c>. Pure: no I/O, no <c>ev1:</c>
/// reference is minted and nothing becomes answerable on the strength of a matching digest.
/// </summary>
public static class RawContentDigest
{
    private static readonly Regex Sha256Lower = new(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

    public static string Compute(ReadOnlySpan<byte> rawBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(rawBytes));

    public static RawContentDigestDecision Verify(ReadOnlySpan<byte> rawBytes, string? claimedDigest)
    {
        if (claimedDigest is null || !Sha256Lower.IsMatch(claimedDigest))
            return RawContentDigestDecision.Refuse("invalid_claimed_digest");
        return string.Equals(Compute(rawBytes), claimedDigest, StringComparison.Ordinal)
            ? RawContentDigestDecision.Match
            : RawContentDigestDecision.Refuse("digest_mismatch");
    }
}
