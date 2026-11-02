using System.Security.Cryptography;
using System.Text;
using System.Xml;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Domain.Parsing;

public enum ParserFailure
{
    UnsupportedFormat,
    Malformed,
    Oversized
}

/// <summary>One answerable text unit. Every chunk keeps its parent <c>ev1:</c> reference.</summary>
public sealed record EvidenceChunk(
    string ParentEvidenceRef, int Ordinal, string ElementId, string Text, string TextSha256);

public sealed record ParserOutput(
    bool Succeeded,
    ParserFailure? Failure,
    string? Reason,
    string? ParentEvidenceRef,
    IReadOnlyList<EvidenceChunk> Chunks)
{
    public static ParserOutput Fail(ParserFailure failure, string reason) =>
        new(false, failure, reason, null, Array.Empty<EvidenceChunk>());
}

/// <summary>
/// VER-P0-07 slice 1 (#157): immutable, versioned parser v1 for the first permitted source
/// (<c>uk-legislation-gov-uk</c>). It reads a deliberately minimal subset: UTF-8 XML whose root
/// is <c>Legislation</c> with <c>SchemaVersion="1"</c>, whose answerable units are
/// <c>P1</c> elements carrying a non-empty <c>id</c>. UNVERIFIED: this subset is not checked
/// against the real publisher format (docs/architecture/FIRST_SOURCE_POLICY.md); a change of
/// accepted input is a new parser version, never an edit of this one. Document text is data
/// only: it is never interpreted, and DTDs and external entities are refused.
/// Fail closed: anything unproven fails with a class and reason.
/// </summary>
public static class UkLegislationParserV1
{
    public const string Name = "uk-legislation-xml";
    public const string Version = "1.0.0";
    public const int MaxBytes = 1_048_576;
    public const int MaxChunks = 2_000;
    public const int MaxChunkChars = 20_000;

    public static ParserOutput Parse(
        byte[] rawBytes, string sourceUri, string recordId, string contentSha256)
    {
        ArgumentNullException.ThrowIfNull(rawBytes);
        if (string.IsNullOrWhiteSpace(sourceUri) || string.IsNullOrWhiteSpace(recordId)
            || string.IsNullOrWhiteSpace(contentSha256))
            return ParserOutput.Fail(ParserFailure.Malformed, "missing_identity_input");
        if (rawBytes.Length > MaxBytes) return ParserOutput.Fail(ParserFailure.Oversized, "raw_too_large");
        if (rawBytes.Length == 0) return ParserOutput.Fail(ParserFailure.Malformed, "empty_input");

        // The digest must be of exactly these bytes; otherwise the reference would bind other content.
        var actual = Convert.ToHexStringLower(SHA256.HashData(rawBytes));
        if (!string.Equals(actual, contentSha256, StringComparison.Ordinal))
            return ParserOutput.Fail(ParserFailure.Malformed, "content_digest_mismatch");

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(rawBytes);
        }
        catch (ArgumentException)
        {
            return ParserOutput.Fail(ParserFailure.Malformed, "invalid_utf8");
        }

        // A valid UTF-8 BOM decodes to U+FEFF, which XmlReader rejects on a string; the digest is of the original bytes.
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        var pending = new List<(string Id, string Text)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), settings);
            if (!reader.Read() || reader.MoveToContent() != XmlNodeType.Element
                || reader.LocalName != "Legislation")
                return ParserOutput.Fail(ParserFailure.UnsupportedFormat, "unsupported_root");
            if (reader.GetAttribute("SchemaVersion") != "1")
                return ParserOutput.Fail(ParserFailure.UnsupportedFormat, "unsupported_schema_version");

            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "P1")
                {
                    reader.Read();
                    continue;
                }
                var id = reader.GetAttribute("id");
                if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
                    return ParserOutput.Fail(ParserFailure.Malformed, "missing_or_invalid_chunk_id");
                if (!seen.Add(id)) return ParserOutput.Fail(ParserFailure.Malformed, "duplicate_chunk_id");
                if (pending.Count >= MaxChunks) return ParserOutput.Fail(ParserFailure.Oversized, "too_many_chunks");
                // Leaves the reader after the element; child markup inside a P1 throws XmlException.
                var content = Normalize(reader.ReadElementContentAsString());
                if (content.Length == 0) return ParserOutput.Fail(ParserFailure.Malformed, "empty_chunk");
                if (content.Length > MaxChunkChars) return ParserOutput.Fail(ParserFailure.Oversized, "chunk_too_large");
                pending.Add((id, content));
            }
        }
        catch (XmlException)
        {
            return ParserOutput.Fail(ParserFailure.Malformed, "malformed_xml");
        }

        if (pending.Count == 0) return ParserOutput.Fail(ParserFailure.Malformed, "no_chunks");

        string parentRef;
        try
        {
            parentRef = EvidenceProvenance.DeriveEvidenceRef(
                EvidenceProvenance.SupportedSchemaVersion,
                EvidenceProvenance.NormalizeSourceUri(sourceUri),
                recordId, contentSha256, Name, Version);
        }
        catch (ProvenanceException error)
        {
            return ParserOutput.Fail(ParserFailure.Malformed, error.Code);
        }

        var chunks = pending
            .Select((c, i) => new EvidenceChunk(
                parentRef, i, c.Id, c.Text,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(c.Text)))))
            .ToList();
        return new ParserOutput(true, null, null, parentRef, chunks);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Evidence is verified and answerable only when the existing envelope verification
    /// accepts it with the retrieval attestation and classification proof present and trusted
    /// and the envelope's reference equals the one this parse derived. Anything else is ineligible.
    /// </summary>
    public static EvidenceVerification Verify(
        ParserOutput output, EvidenceEnvelope envelope, EvidenceTrust trust)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(trust);
        if (!output.Succeeded || output.ParentEvidenceRef is null)
            return EvidenceVerification.Reject("parse_failed");
        if (envelope.ParserName != Name || envelope.ParserVersion != Version)
            return EvidenceVerification.Reject("parser_identity_mismatch");
        var verification = EvidenceProvenance.VerifyEnvelope(envelope, trust);
        if (!verification.Accepted) return verification;
        return verification.EvidenceRef == output.ParentEvidenceRef
            ? verification
            : EvidenceVerification.Reject("evidence_ref_mismatch");
    }
}
