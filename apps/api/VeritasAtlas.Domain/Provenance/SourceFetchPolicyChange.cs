namespace VeritasAtlas.Domain.Provenance;

public sealed record SourceFetchPolicyChangeResult(
    bool Admitted,
    bool Changed,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Widenings);

/// <summary>
/// Pure, fail-closed source change policy for replacing a registered
/// <see cref="SourceFetchPolicy"/> (VER-P0-04 slice 4, #117). A revision must itself pass
/// <see cref="SourceFetchPolicyAdmission"/>, must carry a new policy version that was never
/// used before (so one version always names exactly one policy, and the destination
/// guard's <c>policy_version_drift</c> check stays meaningful), may not re-classify the
/// source's jurisdiction or source type, and may widen the fetch surface only with an
/// explicit approval. Narrowing needs no approval. Re-submitting the identical policy is
/// admitted as a no-op. Every violated rule is reported in a fixed order.
/// </summary>
public static class SourceFetchPolicyChange
{
    public static SourceFetchPolicyChangeResult Evaluate(
        SourceFetchPolicy? current,
        SourceFetchPolicy? proposed,
        IReadOnlyCollection<string>? previousVersions,
        bool wideningApproved)
    {
        if (current is null) return Deny(["current_policy_missing"]);

        var admission = SourceFetchPolicyAdmission.Validate(proposed);
        if (!admission.Admitted) return Deny(admission.Reasons);
        var next = proposed!;

        if (previousVersions is null || previousVersions.Any(v => v is null))
            return Deny(["invalid_version_history"]);

        var sameVersion = string.Equals(current.PolicyVersion, next.PolicyVersion, StringComparison.Ordinal);
        var sameContent = SameContent(current, next);
        if (sameVersion && sameContent) return new(true, false, [], []);

        var reasons = new List<string>();
        if (sameVersion) reasons.Add("policy_version_not_bumped");
        else if (previousVersions.Contains(next.PolicyVersion, StringComparer.Ordinal))
            reasons.Add("policy_version_reused");
        if (!string.Equals(current.Jurisdiction, next.Jurisdiction, StringComparison.Ordinal))
            reasons.Add("jurisdiction_change_forbidden");
        if (!string.Equals(current.SourceType, next.SourceType, StringComparison.Ordinal))
            reasons.Add("source_type_change_forbidden");

        var widenings = Widenings(current, next);
        if (widenings.Count > 0 && !wideningApproved) reasons.Add("widening_requires_approval");

        return new(reasons.Count == 0, true, reasons, widenings);
    }

    private static SourceFetchPolicyChangeResult Deny(IReadOnlyList<string> reasons) =>
        new(false, false, reasons, []);

    private static List<string> Widenings(SourceFetchPolicy current, SourceFetchPolicy next)
    {
        var widenings = new List<string>();
        if (Adds(current.AllowedHosts, next.AllowedHosts, StringComparer.OrdinalIgnoreCase))
            widenings.Add("allowed_hosts_added");
        if (Adds(current.AllowedSchemes, next.AllowedSchemes, StringComparer.Ordinal))
            widenings.Add("allowed_schemes_added");
        if (next.PlaintextHttpException && !current.PlaintextHttpException)
            widenings.Add("plaintext_http_enabled");
        if (Adds(current.AllowedPorts, next.AllowedPorts, EqualityComparer<int>.Default))
            widenings.Add("allowed_ports_added");
        if (next.MaxRedirects > current.MaxRedirects) widenings.Add("max_redirects_increased");
        return widenings;
    }

    /// <summary>A null current set (an invalid legacy row) widens to anything proposed.</summary>
    private static bool Adds<T>(IReadOnlyCollection<T>? current, IReadOnlyCollection<T> next, IEqualityComparer<T> comparer) =>
        next.Except(current ?? [], comparer).Any();

    private static bool SameContent(SourceFetchPolicy current, SourceFetchPolicy next) =>
        string.Equals(current.Jurisdiction, next.Jurisdiction, StringComparison.Ordinal)
        && string.Equals(current.SourceType, next.SourceType, StringComparison.Ordinal)
        && SameSet(current.AllowedHosts, next.AllowedHosts, StringComparer.OrdinalIgnoreCase)
        && SameSet(current.AllowedSchemes, next.AllowedSchemes, StringComparer.Ordinal)
        && SameSet(current.AllowedPorts, next.AllowedPorts, EqualityComparer<int>.Default)
        && current.MaxRedirects == next.MaxRedirects
        && current.PlaintextHttpException == next.PlaintextHttpException;

    private static bool SameSet<T>(IReadOnlyCollection<T>? current, IReadOnlyCollection<T> next, IEqualityComparer<T> comparer) =>
        current is not null && new HashSet<T>(current, comparer).SetEquals(next);
}
