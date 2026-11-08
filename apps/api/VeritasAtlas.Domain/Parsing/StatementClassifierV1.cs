using System.Security.Cryptography;
using System.Text;

namespace VeritasAtlas.Domain.Parsing;

public enum StatementClass
{
    /// <summary>The statement is an exact span of a chunk.</summary>
    Quotation,

    /// <summary>Not a quotation, but every content word is present in one chunk.</summary>
    Inference,

    /// <summary>Hedged wording: the statement asserts nothing that can be checked.</summary>
    Unresolved,

    /// <summary>No chunk supports the statement.</summary>
    Unsupported
}

public enum StatementClassificationFailure
{
    MissingTenant,
    InvalidStatement,
    InvalidChunk
}

/// <summary>
/// Outcome of classifying one statement. <see cref="Span"/> is set only for
/// <see cref="StatementClass.Quotation"/>, <see cref="SupportingEvidenceRef"/> and
/// <see cref="SupportingElementId"/> only for Quotation and Inference.
/// </summary>
public sealed record StatementClassification(
    bool Succeeded,
    StatementClassificationFailure? Failure,
    string? Reason,
    StatementClass? Class,
    ClaimEvidenceSpan? Span,
    string? SupportingEvidenceRef,
    string? SupportingElementId)
{
    public static StatementClassification Fail(StatementClassificationFailure failure, string reason) =>
        new(false, failure, reason, null, null, null, null);

    public static StatementClassification Of(StatementClass cls) =>
        new(true, null, null, cls, null, null, null);

    /// <summary>Only an exact quotation can ever become a claim candidate; an inference never does.</summary>
    public bool CanBecomeClaimCandidate => Succeeded && Class == StatementClass.Quotation;
}

/// <summary>
/// VER-P0-11 slice 3 (#232): deterministic, model-free guard that every statement proposed for
/// claim extraction must pass. Order: exact ordinal substring of a chunk is a
/// <see cref="StatementClass.Quotation"/>; hedged wording is <see cref="StatementClass.Unresolved"/>;
/// every content word present in a single chunk is an <see cref="StatementClass.Inference"/>;
/// everything else (including instruction-like text and invented figures) is
/// <see cref="StatementClass.Unsupported"/>. Fail closed: a chunk whose text does not match its
/// digest, a blank, oversized or control-character statement, or a missing tenant fails the call.
/// The statement is data only and is never interpreted.
/// </summary>
public static class StatementClassifierV1
{
    public const string Name = "statement-classifier-rules";
    public const string Version = "1.0.0";
    public const int MaxStatementLength = 2000;
    public const int MinContentWords = 4;

    // Every distinct content word of the statement must occur in one chunk: a reordering or
    // restructuring of supported words is an inference, a single new word (a new figure, a new
    // actor) is not supported.

    private static readonly HashSet<string> Hedges = new(StringComparer.Ordinal)
    {
        "may", "might", "could", "possibly", "perhaps", "allegedly", "reportedly", "apparently",
        "unclear", "unconfirmed", "rumoured", "rumored", "supposedly", "probably", "likely"
    };

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "at", "by", "for", "with", "is", "are",
        "was", "were", "be", "been", "it", "its", "this", "that", "as", "from", "not", "no"
    };

    public static StatementClassification Classify(
        Guid tenantId, string statement, IReadOnlyList<EvidenceChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (tenantId == Guid.Empty)
            return StatementClassification.Fail(StatementClassificationFailure.MissingTenant, "missing_tenant");
        if (string.IsNullOrWhiteSpace(statement) || statement.Length > MaxStatementLength
            || char.IsWhiteSpace(statement[0]) || char.IsWhiteSpace(statement[^1])
            || statement.Any(char.IsControl))
            return StatementClassification.Fail(StatementClassificationFailure.InvalidStatement, "invalid_statement");

        foreach (var chunk in chunks)
        {
            if (chunk is null || string.IsNullOrWhiteSpace(chunk.ParentEvidenceRef)
                || string.IsNullOrWhiteSpace(chunk.ElementId) || string.IsNullOrEmpty(chunk.Text))
                return StatementClassification.Fail(StatementClassificationFailure.InvalidChunk, "incomplete_chunk");
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chunk.Text)));
            if (!string.Equals(digest, chunk.TextSha256, StringComparison.Ordinal))
                return StatementClassification.Fail(StatementClassificationFailure.InvalidChunk, "chunk_digest_mismatch");
        }

        foreach (var chunk in chunks)
        {
            var start = chunk.Text.IndexOf(statement, StringComparison.Ordinal);
            if (start < 0) continue;
            var span = new ClaimEvidenceSpan(
                chunk.ParentEvidenceRef, chunk.ElementId, chunk.TextSha256, start, statement.Length);
            return StatementClassification.Of(StatementClass.Quotation) with
            {
                Span = span,
                SupportingEvidenceRef = chunk.ParentEvidenceRef,
                SupportingElementId = chunk.ElementId
            };
        }

        var words = Words(statement);
        if (words.Any(Hedges.Contains))
            return StatementClassification.Of(StatementClass.Unresolved);

        var content = words.Where(w => !Stop.Contains(w)).Distinct(StringComparer.Ordinal).ToList();
        if (content.Count >= MinContentWords)
        {
            foreach (var chunk in chunks)
            {
                var have = Words(chunk.Text).ToHashSet(StringComparer.Ordinal);
                if (content.All(have.Contains))
                    return StatementClassification.Of(StatementClass.Inference) with
                    {
                        SupportingEvidenceRef = chunk.ParentEvidenceRef,
                        SupportingElementId = chunk.ElementId
                    };
            }
        }

        return StatementClassification.Of(StatementClass.Unsupported);
    }

    // Letters and digits only, so an invented figure ("9") is a content word that must be present.
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words;
    }
}
