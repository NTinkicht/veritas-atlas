using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Api.Tests;

/// <summary>
/// VER-P0-06 slice 4: the raw content digest is SHA-256 of the exact bytes, lowercase hex, and a
/// claimed digest is verified without any normalisation.
/// </summary>
[TestClass]
public sealed class RawContentDigestTests
{
    private const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string AbcDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [TestMethod]
    public void Published_sha256_vectors_are_reproduced_in_lowercase_hex()
    {
        Assert.AreEqual(EmptyDigest, RawContentDigest.Compute(ReadOnlySpan<byte>.Empty));
        Assert.AreEqual(AbcDigest, RawContentDigest.Compute("abc"u8));
    }

    [TestMethod]
    public void A_single_changed_byte_changes_the_digest()
    {
        var original = RawContentDigest.Compute("abc"u8);
        Assert.AreNotEqual(original, RawContentDigest.Compute("abd"u8));
        Assert.AreNotEqual(original, RawContentDigest.Compute("abc\n"u8));
    }

    [TestMethod]
    public void Hashing_is_over_bytes_not_text()
    {
        byte[] lf = Encoding.ASCII.GetBytes("a\nb");
        byte[] crlf = Encoding.ASCII.GetBytes("a\r\nb");
        Assert.AreNotEqual(RawContentDigest.Compute(lf), RawContentDigest.Compute(crlf));

        byte[] notUtf8 = [0xFF, 0xFE, 0x00, 0xC3, 0x28];
        var digest = RawContentDigest.Compute(notUtf8);
        Assert.AreEqual(64, digest.Length);
        Assert.AreEqual(digest, RawContentDigest.Compute(notUtf8.ToArray()));
        Assert.AreNotEqual(digest, RawContentDigest.Compute(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(notUtf8))));
    }

    [TestMethod]
    public void Matching_lowercase_digest_is_accepted()
    {
        var decision = RawContentDigest.Verify("abc"u8, AbcDigest);
        Assert.IsTrue(decision.Matches);
        Assert.IsNull(decision.Reason);
    }

    [TestMethod]
    public void Well_formed_but_different_digest_is_a_mismatch()
    {
        var decision = RawContentDigest.Verify("abd"u8, AbcDigest);
        Assert.IsFalse(decision.Matches);
        Assert.AreEqual("digest_mismatch", decision.Reason);
    }

    [TestMethod]
    public void Claimed_digest_is_never_normalised()
    {
        foreach (var bad in new string?[]
        {
            null,
            "",
            AbcDigest.ToUpperInvariant(),
            " " + AbcDigest,
            AbcDigest + "\n",
            AbcDigest + " ",
            AbcDigest[..63],
            AbcDigest + "0",
            "sha256:" + AbcDigest,
            new string('g', 64),
        })
        {
            var decision = RawContentDigest.Verify("abc"u8, bad);
            Assert.IsFalse(decision.Matches, $"accepted {bad ?? "null"}");
            Assert.AreEqual("invalid_claimed_digest", decision.Reason, $"reason for {bad ?? "null"}");
        }
    }
}
