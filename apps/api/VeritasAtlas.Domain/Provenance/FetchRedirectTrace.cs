namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// One privacy-safe entry of a redirect audit trace. It never carries the query string,
/// fragment or userinfo of the hop; <see cref="HasQuery"/> only records that a query existed.
/// </summary>
public sealed record FetchRedirectTraceEntry(
    int Index,
    string Scheme,
    string Host,
    int Port,
    string Path,
    bool HasQuery,
    bool AuthorityChanged,
    bool CredentialsMayForward);

public sealed record FetchRedirectTraceResult(
    bool Admitted,
    string? Reason,
    string? FinalUri,
    IReadOnlyList<FetchRedirectTraceEntry> Trace)
{
    public static FetchRedirectTraceResult Deny(string reason) => new(false, reason, null, []);
}

/// <summary>
/// Pure builder for the privacy-safe redirect/audit trace required by
/// <c>docs/EVIDENCE_PROVENANCE_CONTRACT.md</c> › Independent-fetch network policy.
/// The chain is validated by <see cref="FetchDestinationGuard.EvaluateRedirectChain"/> first;
/// any denial yields no trace. Credentials may be forwarded only while every hop so far has
/// exactly the first hop's authority (scheme, host, port; a trailing-dot host is a distinct origin and counts as a change); the flag latches off at the first
/// authority change and stays off even if a later hop returns to the original authority.
/// </summary>
public static class FetchRedirectTrace
{
    public static FetchRedirectTraceResult Build(
        SourceFetchPolicy? policy,
        FetchExpectation? expectation,
        IReadOnlyList<FetchHop>? hops)
    {
        var decision = FetchDestinationGuard.EvaluateRedirectChain(policy, expectation, hops);
        if (!decision.Admitted) return FetchRedirectTraceResult.Deny(decision.Reason ?? "invalid_uri");

        var trace = new List<FetchRedirectTraceEntry>(hops!.Count);
        (string Scheme, string Host, int Port)? origin = null;
        var forward = true;
        for (var i = 0; i < hops.Count; i++)
        {
            // The chain was admitted, so every hop URI parses as an absolute http(s) URI.
            var uri = new Uri(hops[i].Uri!);
            var authority = (uri.Scheme.ToLowerInvariant(), uri.IdnHost.ToLowerInvariant(), uri.Port);
            origin ??= authority;
            var changed = authority != origin.Value;
            if (changed) forward = false;
            trace.Add(new FetchRedirectTraceEntry(
                i, authority.Item1, authority.Item2, authority.Port, uri.AbsolutePath,
                uri.Query.Length > 1, changed, forward));
        }
        return new FetchRedirectTraceResult(true, null, decision.FinalUri, trace);
    }
}
