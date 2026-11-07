namespace VeritasAtlas.Domain.Provenance;

/// <summary>Per-claim summary status; precedence is the declaration order below (first match wins).</summary>
public enum ClaimGraphStatus { Conflicted, Unresolved, Superseded, Agreeing, Isolated }

public sealed record ClaimGraphNode(string ClaimId, string EvidenceRef, string? SourceId, ClaimGraphStatus Status);

public sealed record ClaimRelationGraph(
    string TenantId,
    IReadOnlyList<ClaimGraphNode> Nodes,
    IReadOnlyList<ClaimRelationResult> Edges);

/// <summary>
/// Pure deterministic composition of slice-1 pair classifications into an attributed relation graph
/// (VER-P0-13 slice 2). Unrelated pairs produce no edge. Fail-closed on empty, mixed-tenant, duplicate or
/// oversize input. Output does not depend on input order.
/// </summary>
public static class ClaimRelationGraphBuilder
{
    public const int MaxClaims = 200;

    public static ClaimRelationGraph Build(IReadOnlyList<StructuredClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.Count == 0) throw new ArgumentException("At least one claim is required.", nameof(claims));
        if (claims.Count > MaxClaims) throw new ArgumentException($"At most {MaxClaims} claims per graph.", nameof(claims));
        if (claims.Any(c => c is null)) throw new ArgumentException("Claims must not contain null.", nameof(claims));
        var tenant = claims[0].TenantId;
        if (string.IsNullOrWhiteSpace(tenant) || claims.Any(c => !string.Equals(c.TenantId, tenant, StringComparison.Ordinal)))
            throw new ArgumentException("All claims must belong to the same non-empty tenant.", nameof(claims));
        if (claims.Any(c => string.IsNullOrWhiteSpace(c.ClaimId)))
            throw new ArgumentException("Claim ids must be non-empty.", nameof(claims));

        var sorted = claims.OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToList();
        for (var i = 1; i < sorted.Count; i++)
            if (string.Equals(sorted[i].ClaimId, sorted[i - 1].ClaimId, StringComparison.Ordinal))
                throw new ArgumentException("Duplicate claim id.", nameof(claims));

        var edges = new List<ClaimRelationResult>();
        for (var i = 0; i < sorted.Count; i++)
            for (var j = i + 1; j < sorted.Count; j++)
            {
                var r = ClaimRelationClassifier.Classify(sorted[i], sorted[j]);
                if (r.Relation != ClaimRelation.Unrelated) edges.Add(r);
            }

        var nodes = sorted.Select(c => new ClaimGraphNode(c.ClaimId, c.EvidenceRef, c.SourceId, StatusOf(c.ClaimId, edges))).ToList();
        return new ClaimRelationGraph(tenant, nodes, edges);
    }

    private static ClaimGraphStatus StatusOf(string id, List<ClaimRelationResult> edges)
    {
        var mine = edges.Where(e => e.FirstClaimId == id || e.SecondClaimId == id).ToList();
        if (mine.Any(e => e.Relation == ClaimRelation.Conflicting)) return ClaimGraphStatus.Conflicted;
        if (mine.Any(e => e.Relation == ClaimRelation.Unresolved)) return ClaimGraphStatus.Unresolved;
        if (mine.Any(e => e.Relation == ClaimRelation.Superseded && e.SupersededClaimId == id)) return ClaimGraphStatus.Superseded;
        return mine.Count > 0 ? ClaimGraphStatus.Agreeing : ClaimGraphStatus.Isolated;
    }
}
