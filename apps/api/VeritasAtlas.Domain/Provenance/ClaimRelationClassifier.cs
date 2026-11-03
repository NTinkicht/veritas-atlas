using System.Globalization;
using System.Text;

namespace VeritasAtlas.Domain.Provenance;

public enum ClaimRelation { Unrelated, Agreeing, Conflicting, Superseded, Unresolved }

public static class ClaimRelationReasons
{
    public const string DifferentSubject = "different_subject";
    public const string DifferentPredicate = "different_predicate";
    public const string EntityUnresolved = "entity_unresolved";
    public const string EntityKeyMismatch = "entity_key_mismatch";
    public const string SameValueSamePolarity = "same_value_same_polarity";
    public const string PolarityConflict = "polarity_conflict";
    public const string ValueConflictSameAsOf = "value_conflict_same_as_of";
    public const string LaterAsOfSupersedes = "later_as_of_supersedes";
    public const string AsOfMissing = "as_of_missing";
    public const string BothNegatedDifferentValue = "both_negated_different_value";
    public const string OppositePolarityDifferentValue = "opposite_polarity_different_value";
}

/// <summary>A structured, tenant-scoped factual claim. <see cref="EntityKey"/> is the resolved canonical entity id, if any.</summary>
public sealed record StructuredClaim(
    string TenantId, string ClaimId, string EvidenceRef, string? SourceId,
    string Subject, string? EntityKey, string Predicate, bool Negated, string? Value, DateOnly? AsOf);

public sealed record ClaimRelationResult(
    ClaimRelation Relation, string Reason,
    string FirstClaimId, string SecondClaimId,
    string FirstEvidenceRef, string SecondEvidenceRef,
    string? FirstSourceId, string? SecondSourceId,
    string? SupersededClaimId, string? SupersedingClaimId);

/// <summary>
/// Pure deterministic pair classifier (VER-P0-13 slice 1). Fail-closed: when a conflict cannot be
/// distinguished from supersession or the entity is not resolved, the result is Unresolved, never a guess.
/// </summary>
public static class ClaimRelationClassifier
{
    public static ClaimRelationResult Classify(StructuredClaim a, StructuredClaim b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (string.IsNullOrWhiteSpace(a.TenantId) || !string.Equals(a.TenantId, b.TenantId, StringComparison.Ordinal))
            throw new ArgumentException("Claims must belong to the same non-empty tenant.");
        if (string.Equals(a.ClaimId, b.ClaimId, StringComparison.Ordinal))
            throw new ArgumentException("A claim cannot be compared with itself.");

        if (Norm(a.Subject) != Norm(b.Subject)) return R(a, b, ClaimRelation.Unrelated, ClaimRelationReasons.DifferentSubject);
        if (Norm(a.Predicate) != Norm(b.Predicate)) return R(a, b, ClaimRelation.Unrelated, ClaimRelationReasons.DifferentPredicate);
        if (a.EntityKey is null || b.EntityKey is null || Norm(a.EntityKey).Length == 0 || Norm(b.EntityKey).Length == 0)
            return R(a, b, ClaimRelation.Unresolved, ClaimRelationReasons.EntityUnresolved);
        // Same name, different resolved entity: a name collision, not a contradiction.
        if (!string.Equals(a.EntityKey, b.EntityKey, StringComparison.Ordinal))
            return R(a, b, ClaimRelation.Unrelated, ClaimRelationReasons.EntityKeyMismatch);

        var sameValue = Norm(a.Value) == Norm(b.Value);
        if (sameValue)
        {
            if (a.Negated == b.Negated) return R(a, b, ClaimRelation.Agreeing, ClaimRelationReasons.SameValueSamePolarity);
            // Opposite polarity may describe a change over time, so apply temporal disambiguation first.
            if (a.AsOf is null || b.AsOf is null) return R(a, b, ClaimRelation.Unresolved, ClaimRelationReasons.AsOfMissing);
            if (a.AsOf == b.AsOf) return R(a, b, ClaimRelation.Conflicting, ClaimRelationReasons.PolarityConflict);
            var (o, n) = a.AsOf < b.AsOf ? (a, b) : (b, a);
            return R(a, b, ClaimRelation.Superseded, ClaimRelationReasons.LaterAsOfSupersedes, o.ClaimId, n.ClaimId);
        }

        // Different values. Two negations of different values do not contradict each other.
        if (a.Negated && b.Negated) return R(a, b, ClaimRelation.Unresolved, ClaimRelationReasons.BothNegatedDifferentValue);
        // Opposite polarity with different values is not a direct contradiction either.
        if (a.Negated != b.Negated) return R(a, b, ClaimRelation.Unresolved, ClaimRelationReasons.OppositePolarityDifferentValue);

        if (a.AsOf is null || b.AsOf is null) return R(a, b, ClaimRelation.Unresolved, ClaimRelationReasons.AsOfMissing);
        if (a.AsOf == b.AsOf) return R(a, b, ClaimRelation.Conflicting, ClaimRelationReasons.ValueConflictSameAsOf);
        var (older, newer) = a.AsOf < b.AsOf ? (a, b) : (b, a);
        return R(a, b, ClaimRelation.Superseded, ClaimRelationReasons.LaterAsOfSupersedes, older.ClaimId, newer.ClaimId);
    }

    private static ClaimRelationResult R(StructuredClaim a, StructuredClaim b, ClaimRelation rel, string reason,
        string? superseded = null, string? superseding = null) =>
        new(rel, reason, a.ClaimId, b.ClaimId, a.EvidenceRef, b.EvidenceRef, a.SourceId, b.SourceId, superseded, superseding);

    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new StringBuilder();
        var space = false;
        foreach (var ch in s.Normalize(NormalizationForm.FormKC).Trim())
        {
            if (char.IsWhiteSpace(ch)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
