using System.Net;
using System.Net.Sockets;

namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// A registered public-record source policy for independent server-side fetches
/// (VER-P0-04 slice 1, #111). Hosts are exact (no wildcards) and compared
/// case-insensitively; only the listed schemes and ports are admitted. Plain
/// <c>http</c> additionally needs <see cref="PlaintextHttpException"/>, a separately
/// approved exception (VER-P0-04 slice 2, #113); listing the scheme alone is not enough.
/// </summary>
public sealed record SourceFetchPolicy(
    string PolicyVersion,
    string Jurisdiction,
    string SourceType,
    IReadOnlyCollection<string> AllowedHosts,
    IReadOnlyCollection<string> AllowedSchemes,
    IReadOnlyCollection<int> AllowedPorts,
    int MaxRedirects,
    bool PlaintextHttpException = false);

/// <summary>What the caller expects to fetch: the registry policy version and classification.</summary>
public sealed record FetchExpectation(string PolicyVersion, string Jurisdiction, string SourceType);

/// <summary>One connection target: the URI and the addresses the trusted resolver returned for it.</summary>
public sealed record FetchHop(string? Uri, IReadOnlyCollection<IPAddress>? ResolvedAddresses);

public sealed record FetchDestinationDecision(bool Admitted, string? Reason, string? FinalUri)
{
    public static FetchDestinationDecision Deny(string reason) => new(false, reason, null);
}

/// <summary>
/// Pure, fail-closed destination policy from <c>docs/EVIDENCE_PROVENANCE_CONTRACT.md</c>
/// › Independent-fetch network policy. It performs no DNS or network I/O: the trusted
/// resolver's answer is an explicit input, and every redirect hop is re-validated.
/// This is destination enforcement, separate from URI identity normalization.
/// </summary>
public static class FetchDestinationGuard
{
    public static FetchDestinationDecision EvaluateHop(
        SourceFetchPolicy? policy,
        FetchExpectation? expectation,
        FetchHop? hop)
    {
        var classification = CheckClassification(policy, expectation);
        if (classification is not null) return FetchDestinationDecision.Deny(classification);
        return CheckHop(policy!, hop);
    }

    /// <summary>
    /// Evaluates the requested URI followed by each redirect target, in order. The first
    /// denial wins; more than <see cref="SourceFetchPolicy.MaxRedirects"/> redirects is denied, and so is a redirect from <c>https</c> down to <c>http</c> even when a
    /// plaintext exception is approved (<c>scheme_downgrade</c>).
    /// </summary>
    public static FetchDestinationDecision EvaluateRedirectChain(
        SourceFetchPolicy? policy,
        FetchExpectation? expectation,
        IReadOnlyList<FetchHop>? hops)
    {
        var classification = CheckClassification(policy, expectation);
        if (classification is not null) return FetchDestinationDecision.Deny(classification);
        if (hops is null || hops.Count == 0) return FetchDestinationDecision.Deny("invalid_uri");
        if (policy!.MaxRedirects < 0 || hops.Count - 1 > policy.MaxRedirects)
            return FetchDestinationDecision.Deny("too_many_redirects");
        FetchDestinationDecision last = FetchDestinationDecision.Deny("invalid_uri");
        string? previousScheme = null;
        foreach (var hop in hops)
        {
            last = CheckHop(policy, hop);
            if (!last.Admitted) return last;
            // CheckHop admitted the hop, so its URI parses and carries http or https.
            var scheme = new Uri(last.FinalUri!).Scheme.ToLowerInvariant();
            if (previousScheme == "https" && scheme == "http")
                return FetchDestinationDecision.Deny("scheme_downgrade");
            previousScheme = scheme;
        }
        return last;
    }

    private static string? CheckClassification(SourceFetchPolicy? policy, FetchExpectation? expectation)
    {
        if (policy is null || expectation is null || string.IsNullOrEmpty(policy.PolicyVersion))
            return "policy_version_drift";
        if (!string.Equals(policy.PolicyVersion, expectation.PolicyVersion, StringComparison.Ordinal))
            return "policy_version_drift";
        if (string.IsNullOrEmpty(policy.Jurisdiction)
            || !string.Equals(policy.Jurisdiction, expectation.Jurisdiction, StringComparison.Ordinal))
            return "wrong_jurisdiction";
        if (string.IsNullOrEmpty(policy.SourceType)
            || !string.Equals(policy.SourceType, expectation.SourceType, StringComparison.Ordinal))
            return "wrong_source_type";
        return null;
    }

    private static FetchDestinationDecision CheckHop(SourceFetchPolicy policy, FetchHop? hop)
    {
        if (hop?.Uri is null
            || !System.Uri.TryCreate(hop.Uri, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host))
            return FetchDestinationDecision.Deny("invalid_uri");

        var scheme = uri.Scheme.ToLowerInvariant();
        if ((scheme != "https" && scheme != "http")
            || policy.AllowedSchemes is null
            || !policy.AllowedSchemes.Any(s => string.Equals(s, scheme, StringComparison.OrdinalIgnoreCase))
            || (scheme == "http" && !policy.PlaintextHttpException))
            return FetchDestinationDecision.Deny("scheme_not_allowed");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return FetchDestinationDecision.Deny("credentials_in_uri");

        var host = uri.IdnHost.TrimEnd('.');
        if (policy.AllowedHosts is null
            || !policy.AllowedHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
            return FetchDestinationDecision.Deny("host_not_allowed");

        if (policy.AllowedPorts is null || !policy.AllowedPorts.Contains(uri.Port))
            return FetchDestinationDecision.Deny("port_not_allowed");

        if (hop.ResolvedAddresses is null || hop.ResolvedAddresses.Count == 0)
            return FetchDestinationDecision.Deny("unresolved_destination");
        if (hop.ResolvedAddresses.Any(address => address is null || IsForbiddenAddress(address)))
            return FetchDestinationDecision.Deny("forbidden_address");

        return new FetchDestinationDecision(true, null, uri.AbsoluteUri);
    }

    /// <summary>
    /// True for unspecified, loopback, private, carrier-grade NAT, link-local (including
    /// cloud metadata), multicast, broadcast/reserved and "this network" ranges, for IPv4,
    /// IPv6 and IPv4-mapped IPv6. Anything that is neither IPv4 nor IPv6 is forbidden.
    /// </summary>
    public static bool IsForbiddenAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                   // 0.0.0.0/8 "this network"
                || b[0] == 10                                  // 10/8 private
                || b[0] == 127                                 // loopback
                || (b[0] == 100 && (b[1] & 0xC0) == 64)        // 100.64/10 CGNAT
                || (b[0] == 169 && b[1] == 254)                // link-local + metadata
                || (b[0] == 172 && (b[1] & 0xF0) == 16)        // 172.16/12 private
                || (b[0] == 192 && b[1] == 168)                // 192.168/16 private
                || (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2)) // 192.0.0/24 IETF (192.0.0.192 metadata), 192.0.2/24 docs
                || (b[0] == 198 && (b[1] & 0xFE) == 18)        // 198.18/15 benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)  // 198.51.100/24 documentation
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)   // 203.0.113/24 documentation
                || b[0] >= 224;                                // multicast, 240/4, broadcast
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            if (b.Take(12).All(x => x == 0))                   // ::/96: unspecified, loopback, IPv4-compatible
                return true;
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4] == 0x00 && b[5] == 0x01)
                return true;                                         // 64:ff9b:1::/48 local-use NAT64
            if (b[0] == 0x01 && b[1] == 0x00 && b.Skip(2).Take(6).All(x => x == 0))
                return true;                                         // 100::/64 discard-only
            if (b[0] == 0x20 && b[1] == 0x01 && (b[2] & 0xFE) == 0x00)
                return true;                                         // 2001::/23 IETF assignments incl. Teredo
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
                return true;                                         // 2001:db8::/32 documentation
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.Skip(4).Take(8).All(x => x == 0))
                return IsForbiddenAddress(new IPAddress(b[12..16])); // 64:ff9b::/96 NAT64
            if (b[0] == 0x20 && b[1] == 0x02)
                return IsForbiddenAddress(new IPAddress(b[2..6]));   // 2002::/16 6to4
            return (b[0] & 0xFE) == 0xFC                      // fc00::/7 unique local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80)     // fe80::/10 link-local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0)     // fec0::/10 site-local (deprecated)
                || b[0] == 0xFF;                               // multicast
        }
        return true;
    }
}
