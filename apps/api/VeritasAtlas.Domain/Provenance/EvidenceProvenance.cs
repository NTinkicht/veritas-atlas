using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// Flat evidence-provenance envelope v1 as defined in
/// <c>docs/EVIDENCE_PROVENANCE_CONTRACT.md</c> and pinned by
/// <c>tests/fixtures/evidence_provenance_v1.json</c>. A null member is a missing field.
/// </summary>
public sealed record EvidenceEnvelope(
    string? SchemaVersion,
    string? EvidenceRef,
    string? SourceUri,
    string? RecordId,
    string? ContentSha256,
    string? ParserName,
    string? ParserVersion,
    string? RetrievedAt,
    string? RetrievalAttestation,
    string? Jurisdiction,
    string? SourceType,
    string? ClassificationProof);

/// <summary>Trust decisions made by the trusted boundaries, supplied explicitly.</summary>
public sealed record EvidenceTrust(bool Retrieval, bool Classification, bool Digest);

public sealed record EvidenceVerification(
    bool Accepted,
    string? Reason,
    string? NormalizedSourceUri,
    string? EvidenceRef)
{
    public static EvidenceVerification Reject(string reason) => new(false, reason, null, null);
}

public sealed class ProvenanceException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// VER-P0-05: pure deterministic source-URI normalization and canonical <c>ev1:</c>
/// evidence reference derivation. Mirrors the offline reference verifier
/// <c>scripts/evidence_provenance.py</c>, including its reason codes; both are bound
/// to the same checked-in vectors. Anything the rules cannot prove fails closed.
/// </summary>
public static class EvidenceProvenance
{
    public const string SupportedSchemaVersion = "1.0";

    private const string Unreserved =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    private static readonly Regex Sha256Pattern =
        new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    private static readonly Regex SchemePattern =
        new("^[A-Za-z][A-Za-z0-9+.-]*$", RegexOptions.CultureInvariant);

    private static readonly Regex SecretQueryPattern = new(
        "(?:^|[?&;])[^=&]*(?:password|passwd|secret|token|api[_-]?key|authorization|credential)[^=&]*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // RFC 3339 with an explicit UTC offset (Z or +/-00:00).
    private static readonly Regex RetrievedAtPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]00:00)$",
        RegexOptions.CultureInvariant);

    public static string NormalizeSourceUri(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) throw new ProvenanceException("missing_source_uri");
        // Whitespace and control characters are never silently stripped.
        if (raw.Any(c => c <= ' ' || c == '\u007f')) throw new ProvenanceException("malformed_source_uri");

        var colon = raw.IndexOf(':');
        if (colon <= 0 || !SchemePattern.IsMatch(raw[..colon]))
            throw new ProvenanceException("malformed_source_uri");
        var scheme = raw[..colon].ToLowerInvariant();
        var rest = raw[(colon + 1)..];
        if (scheme is not ("http" or "https") || !rest.StartsWith("//", StringComparison.Ordinal))
            throw new ProvenanceException("malformed_source_uri");
        rest = rest[2..];

        var fragmentAt = rest.IndexOf('#');
        if (fragmentAt >= 0) rest = rest[..fragmentAt];
        var authorityEnd = rest.IndexOfAny(['/', '?']);
        var authority = authorityEnd < 0 ? rest : rest[..authorityEnd];
        var pathAndQuery = authorityEnd < 0 ? "" : rest[authorityEnd..];
        var queryAt = pathAndQuery.IndexOf('?');
        var rawPath = queryAt < 0 ? pathAndQuery : pathAndQuery[..queryAt];
        var rawQuery = queryAt < 0 ? "" : pathAndQuery[(queryAt + 1)..];

        if (authority.Contains('@')) throw new ProvenanceException("source_uri_userinfo_forbidden");
        var (host, port) = SplitHostPort(authority);
        if (host.Any(c => c > '\u007f'))
            // The pinned idna-uts46-nontransitional-15.1-v1 profile cannot be proven
            // with the runtime's ambient IDNA tables, so verification must fail.
            throw new ProvenanceException("unsupported_idna_profile");
        host = host.ToLowerInvariant();
        if (host.Contains(':')) host = $"[{host}]";

        var isDefaultPort = port is null || (scheme == "http" && port == 80) || (scheme == "https" && port == 443);
        var normalizedAuthority = isDefaultPort ? host : $"{host}:{port!.Value.ToString(CultureInfo.InvariantCulture)}";
        var path = RemoveDotSegments(NormalizePercent(rawPath.Length == 0 ? "/" : rawPath));
        var query = NormalizePercent(rawQuery);
        if (query.Length > 0 && SecretQueryPattern.IsMatch("?" + query))
            throw new ProvenanceException("secret_bearing_query");
        return query.Length == 0
            ? $"{scheme}://{normalizedAuthority}{path}"
            : $"{scheme}://{normalizedAuthority}{path}?{query}";
    }

    public static string DeriveEvidenceRef(
        string schemaVersion,
        string sourceUri,
        string recordId,
        string contentSha256,
        string parserName,
        string parserVersion)
    {
        // RFC 8785 for a flat object of JSON strings: members sorted by UTF-16 code
        // units (this fixed key order already is) and ECMAScript string escaping.
        var canonical = new StringBuilder("{");
        AppendMember(canonical, "content_sha256", contentSha256, first: true);
        AppendMember(canonical, "parser_name", parserName);
        AppendMember(canonical, "parser_version", parserVersion);
        AppendMember(canonical, "record_id", recordId);
        AppendMember(canonical, "schema_version", schemaVersion);
        AppendMember(canonical, "source_uri", sourceUri);
        canonical.Append('}');

        byte[] bytes;
        try
        {
            bytes = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(canonical.ToString());
        }
        catch (EncoderFallbackException)
        {
            throw new ProvenanceException("malformed_identity_text");
        }
        return "ev1:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static EvidenceVerification VerifyEnvelope(EvidenceEnvelope envelope, EvidenceTrust trust)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(trust);
        try
        {
            var schemaVersion = RequireText(envelope.SchemaVersion, "missing_schema_version");
            if (schemaVersion != SupportedSchemaVersion) throw new ProvenanceException("unsupported_schema_version");
            var normalizedUri = NormalizeSourceUri(RequireText(envelope.SourceUri, "missing_source_uri"));
            var recordId = RequireText(envelope.RecordId, "missing_record_id");
            var digest = RequireText(envelope.ContentSha256, "missing_content_digest");
            if (!Sha256Pattern.IsMatch(digest)) throw new ProvenanceException("invalid_sha256");
            if (!trust.Digest) throw new ProvenanceException("content_digest_unverified");
            var parserName = RequireText(envelope.ParserName, "missing_parser_name");
            var parserVersion = RequireText(envelope.ParserVersion, "missing_parser_version");
            ValidateRetrievedAt(RequireText(envelope.RetrievedAt, "missing_retrieved_at"));
            RequireText(envelope.RetrievalAttestation, "missing_trusted_retrieval_attestation");
            if (!trust.Retrieval) throw new ProvenanceException("untrusted_retrieval");
            RequireText(envelope.Jurisdiction, "missing_jurisdiction");
            RequireText(envelope.SourceType, "missing_source_type");
            RequireText(envelope.ClassificationProof, "missing_classification_proof");
            if (!trust.Classification) throw new ProvenanceException("untrusted_classification");

            var derived = DeriveEvidenceRef(schemaVersion, normalizedUri, recordId, digest, parserName, parserVersion);
            if (envelope.EvidenceRef is not null && envelope.EvidenceRef != derived)
                throw new ProvenanceException("evidence_ref_mismatch");
            return new EvidenceVerification(true, null, normalizedUri, derived);
        }
        catch (ProvenanceException error)
        {
            return EvidenceVerification.Reject(error.Code);
        }
    }

    private static (string Host, int? Port) SplitHostPort(string authority)
    {
        string host;
        string portText;
        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            if (close < 0) throw new ProvenanceException("malformed_source_uri");
            host = authority[1..close];
            var after = authority[(close + 1)..];
            if (after.Length > 0 && after[0] != ':') throw new ProvenanceException("malformed_source_uri");
            portText = after.Length > 0 ? after[1..] : "";
            if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ProvenanceException("malformed_source_uri");
        }
        else
        {
            if (authority.Contains('[') || authority.Contains(']')) throw new ProvenanceException("malformed_source_uri");
            var portAt = authority.IndexOf(':');
            host = portAt < 0 ? authority : authority[..portAt];
            portText = portAt < 0 ? "" : authority[(portAt + 1)..];
        }
        if (host.Length == 0) throw new ProvenanceException("malformed_source_uri");
        if (portText.Length == 0) return (host, null);
        if (!portText.All(char.IsAsciiDigit)
            || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port > 65535)
            throw new ProvenanceException("malformed_source_uri");
        return (host, port);
    }

    private static string NormalizePercent(string value)
    {
        var output = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (value[index] != '%')
            {
                output.Append(value[index]);
                index++;
                continue;
            }
            if (index + 2 >= value.Length || !char.IsAsciiHexDigit(value[index + 1]) || !char.IsAsciiHexDigit(value[index + 2]))
                throw new ProvenanceException("malformed_percent_escape");
            var octet = Convert.ToInt32(value.Substring(index + 1, 2), 16);
            var decoded = (char)octet;
            if (Unreserved.Contains(decoded)) output.Append(decoded);
            else output.Append('%').Append(octet.ToString("X2", CultureInfo.InvariantCulture));
            index += 3;
        }
        return output.ToString();
    }

    // Same segment rules as the reference verifier: empty and "." segments are
    // dropped, ".." pops, leading and trailing slashes are kept.
    private static string RemoveDotSegments(string path)
    {
        var leading = path.StartsWith('/');
        var trailing = path.EndsWith('/') && path != "/";
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        var normalized = (leading ? "/" : "") + string.Join('/', segments);
        if (normalized.Length == 0) return "/";
        if (trailing && !normalized.EndsWith('/')) normalized += "/";
        return normalized;
    }

    private static string RequireText(string? value, string code) =>
        string.IsNullOrEmpty(value) ? throw new ProvenanceException(code) : value;

    private static void ValidateRetrievedAt(string value)
    {
        if (!RetrievedAtPattern.IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ProvenanceException("invalid_retrieved_at");
    }

    private static void AppendMember(StringBuilder builder, string key, string value, bool first = false)
    {
        if (!first) builder.Append(',');
        AppendJsonString(builder, key);
        builder.Append(':');
        AppendJsonString(builder, value);
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}
