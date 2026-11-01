using System.Text.RegularExpressions;

namespace VeritasAtlas.Domain.Provenance;

public sealed record SourceFetchPolicyAdmissionResult(bool Admitted, IReadOnlyList<string> Reasons);

/// <summary>
/// Pure, fail-closed connector admission for <see cref="SourceFetchPolicy"/> (VER-P0-04
/// slice 2, #113). A policy is validated before it may be registered, so the destination
/// guard only ever evaluates well-formed policies: exact lowercase DNS hosts (never IP
/// literals, wildcards or local-only names), HTTP(S) schemes with plain <c>http</c> only
/// under an explicit exception, real ports and a bounded redirect count. Every violated
/// rule is reported, in a fixed order, as a stable reason code.
/// </summary>
public static partial class SourceFetchPolicyAdmission
{
    public const int MaxAllowedHosts = 32;
    public const int MaxRedirectLimit = 10;
    public const int MaxHostLength = 253;

    private static readonly string[] ForbiddenHostSuffixes = [".localhost", ".local", ".internal", ".arpa"];

    public static SourceFetchPolicyAdmissionResult Validate(SourceFetchPolicy? policy)
    {
        if (policy is null) return new(false, ["policy_missing"]);

        var reasons = new List<string>();
        if (policy.PolicyVersion is null || !PolicyVersionPattern().IsMatch(policy.PolicyVersion))
            reasons.Add("invalid_policy_version");
        if (policy.Jurisdiction is null || !JurisdictionPattern().IsMatch(policy.Jurisdiction))
            reasons.Add("invalid_jurisdiction");
        if (policy.SourceType is null || !SourceTypePattern().IsMatch(policy.SourceType))
            reasons.Add("invalid_source_type");

        var hosts = policy.AllowedHosts;
        if (hosts is null || hosts.Count == 0)
        {
            reasons.Add("no_allowed_hosts");
        }
        else
        {
            if (hosts.Count > MaxAllowedHosts) reasons.Add("too_many_allowed_hosts");
            if (hosts.Any(h => !IsValidHost(h))) reasons.Add("invalid_allowed_host");
            if (hosts.Any(h => IsValidHost(h) && IsForbiddenHost(h))) reasons.Add("forbidden_allowed_host");
            if (hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hosts.Count)
                reasons.Add("duplicate_allowed_host");
        }

        var schemes = policy.AllowedSchemes;
        if (schemes is null || schemes.Count == 0
            || schemes.Any(s => s is not ("https" or "http"))
            || schemes.Distinct(StringComparer.Ordinal).Count() != schemes.Count)
            reasons.Add("invalid_allowed_scheme");
        if (schemes is not null && schemes.Contains("http") && !policy.PlaintextHttpException)
            reasons.Add("http_without_exception");

        var ports = policy.AllowedPorts;
        if (ports is null || ports.Count == 0
            || ports.Any(p => p is < 1 or > 65535)
            || ports.Distinct().Count() != ports.Count)
            reasons.Add("invalid_allowed_port");

        if (policy.MaxRedirects is < 0 or > MaxRedirectLimit) reasons.Add("invalid_max_redirects");

        return new(reasons.Count == 0, reasons);
    }

    /// <summary>
    /// A lowercase ASCII DNS name (IDN as punycode) with at least two labels, no trailing
    /// dot or wildcard, and a non-numeric last label (so IPv4 literals are rejected; IPv6
    /// literals fail the character set).
    /// </summary>
    private static bool IsValidHost(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > MaxHostLength) return false;
        var labels = host.Split('.');
        if (labels.Length < 2) return false;
        if (!labels.All(label => HostLabelPattern().IsMatch(label))) return false;
        return !labels[^1].All(char.IsAsciiDigit);
    }

    private static bool IsForbiddenHost(string host) =>
        ForbiddenHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal));

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HostLabelPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyVersionPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex JurisdictionPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SourceTypePattern();
}
