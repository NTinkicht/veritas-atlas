using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Domain.Parsing;

namespace VeritasAtlas.Api.Tests;

/// <summary>VER-P0-11 slice (#165). All fixtures are hand-written and SYNTHETIC.</summary>
[TestClass]
public sealed class ClaimExtractionV1Tests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly ClaimExtractionV1 _sut = new();

    private static EvidenceChunk Chunk(string text, string id = "s1", string parent = "ev1:aa")
        => new(parent, 0, id, text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));

    private const string Text = "The operator shall keep records. This is background. The operator must not alter them; Notes follow.";

    [TestMethod]
    public void Extracts_only_normative_sentences_with_exact_spans()
    {
        var chunk = Chunk(Text);
        var r = _sut.Extract(TenantA, new[] { chunk });
        Assert.IsTrue(r.Succeeded);
        Assert.AreEqual(2, r.Claims.Count);
        Assert.AreEqual("The operator shall keep records.", r.Claims[0].Quotation);
        Assert.AreEqual("The operator must not alter them;", r.Claims[1].Quotation);
        foreach (var c in r.Claims)
        {
            Assert.AreEqual(ClaimStatus.Unconfirmed, c.Status);
            Assert.AreEqual(TenantA, c.TenantId);
            Assert.AreEqual("ev1:aa", c.Span.ParentEvidenceRef);
            Assert.IsTrue(ClaimExtractionV1.Verify(c, chunk));
        }
    }

    [TestMethod]
    public void Is_deterministic()
    {
        var a = _sut.Extract(TenantA, new[] { Chunk(Text) });
        var b = _sut.Extract(TenantA, new[] { Chunk(Text) });
        CollectionAssert.AreEqual(a.Claims.Select(c => c.ClaimKey).ToList(), b.Claims.Select(c => c.ClaimKey).ToList());
    }

    [TestMethod]
    public void Claim_keys_are_tenant_scoped()
    {
        var a = _sut.Extract(TenantA, new[] { Chunk(Text) }).Claims[0].ClaimKey;
        var b = _sut.Extract(TenantB, new[] { Chunk(Text) }).Claims[0].ClaimKey;
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Fails_closed_on_missing_tenant()
    {
        var r = _sut.Extract(Guid.Empty, new[] { Chunk(Text) });
        Assert.IsFalse(r.Succeeded);
        Assert.AreEqual(ClaimExtractionFailure.MissingTenant, r.Failure);
        Assert.AreEqual(0, r.Claims.Count);
    }

    [TestMethod]
    public void Fails_closed_on_digest_mismatch_or_incomplete_chunk_without_partial_claims()
    {
        var good = Chunk(Text);
        var bad = good with { TextSha256 = new string('0', 64) };
        var r = _sut.Extract(TenantA, new[] { good, bad });
        Assert.IsFalse(r.Succeeded);
        Assert.AreEqual("chunk_digest_mismatch", r.Reason);
        Assert.AreEqual(0, r.Claims.Count);
        Assert.AreEqual("incomplete_chunk", _sut.Extract(TenantA, new[] { good with { ElementId = " " } }).Reason);
        Assert.AreEqual("incomplete_chunk", _sut.Extract(TenantA, new[] { good with { ParentEvidenceRef = "" } }).Reason);
    }

    [TestMethod]
    public void No_normative_text_yields_no_claims()
    {
        var r = _sut.Extract(TenantA, new[] { Chunk("Plain background text. Nothing binding here.") });
        Assert.IsTrue(r.Succeeded);
        Assert.AreEqual(0, r.Claims.Count);
    }

    [TestMethod]
    public void Verify_rejects_tampered_quotation_span_or_other_chunk()
    {
        var chunk = Chunk(Text);
        var claim = _sut.Extract(TenantA, new[] { chunk }).Claims[0];
        Assert.IsFalse(ClaimExtractionV1.Verify(claim with { Quotation = "The operator shall keep records" }, chunk));
        Assert.IsFalse(ClaimExtractionV1.Verify(claim with { Span = claim.Span with { Start = 1 } }, chunk));
        Assert.IsFalse(ClaimExtractionV1.Verify(claim with { Span = claim.Span with { Length = 9999 } }, chunk));
        Assert.IsFalse(ClaimExtractionV1.Verify(claim, Chunk(Text, id: "s2")));
        Assert.IsFalse(ClaimExtractionV1.Verify(claim, Chunk(Text, parent: "ev1:bb")));
        Assert.IsFalse(ClaimExtractionV1.Verify(claim, chunk with { TextSha256 = new string('0', 64) }));
    }

    [TestMethod]
    public void Unconfirmed_claims_are_never_answerable_and_confirmed_ones_only_for_their_tenant()
    {
        var chunk = Chunk(Text);
        var claim = _sut.Extract(TenantA, new[] { chunk }).Claims[0];
        Assert.IsFalse(ClaimExtractionV1.IsAnswerable(claim, TenantA, chunk));
        var confirmed = ClaimExtractionV1.Confirm(claim);
        Assert.IsTrue(ClaimExtractionV1.IsAnswerable(confirmed, TenantA, chunk));
        Assert.IsFalse(ClaimExtractionV1.IsAnswerable(confirmed, TenantB, chunk));
        Assert.IsFalse(ClaimExtractionV1.IsAnswerable(confirmed, Guid.Empty, chunk));
        Assert.IsFalse(ClaimExtractionV1.IsAnswerable(confirmed with { Quotation = "forged" }, TenantA, chunk));
    }
}
