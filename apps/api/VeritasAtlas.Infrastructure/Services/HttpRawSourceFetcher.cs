using System.Net;
using System.Net.Sockets;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Infrastructure.Services;

/// <summary>
/// VER-P0-06 (#156). Composes the pure provenance guards into one fail-closed retrieval.
/// Redirects are followed manually; each hop is resolved and checked against the tenant's
/// current <see cref="SourceFetchPolicy"/> before any request is sent. No credentials,
/// cookies or other caller secrets are ever attached, so nothing can cross an authority
/// change. The injected <see cref="HttpClient"/> must have auto-redirect disabled and connect only
/// to vetted addresses (see <see cref="GuardedConnect"/>).
/// </summary>
public sealed class HttpRawSourceFetcher : IRawSourceFetcher
{
    private const int ReadChunk = 16 * 1024;
    private const int MaxDedupLookback = 20;

    private readonly HttpClient _http;
    private readonly IHostAddressResolver _resolver;
    private readonly ITenantSourceFetchPolicyRegistry _policies;
    private readonly ITenantFetchAttemptLedger _attempts;
    private readonly ITenantRawRetrievalLedger _raw;
    private readonly TimeProvider _time;

    public HttpRawSourceFetcher(
        HttpClient http,
        IHostAddressResolver resolver,
        ITenantSourceFetchPolicyRegistry policies,
        ITenantFetchAttemptLedger attempts,
        ITenantRawRetrievalLedger raw,
        TimeProvider time)
    {
        _http = http;
        _resolver = resolver;
        _policies = policies;
        _attempts = attempts;
        _raw = raw;
        _time = time;
    }

    public async Task<RawSourceFetchResult> FetchAsync(
        Guid tenantId, RawSourceFetchRequest request, string actorSubject, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actorSubject)) throw new ArgumentException("Actor is required.", nameof(actorSubject));
        if (string.IsNullOrWhiteSpace(request.AttemptKey)
            || request.AttemptKey.Length > TenantFetchAttemptLimits.MaxAttemptKeyLength)
            throw new ArgumentException("Attempt key is invalid.", nameof(request));

        var current = await _policies.GetCurrentAsync(tenantId, request.SourceEntryId, cancellationToken);
        if (current is null) return Result(RawSourceFetchStatus.SourceNotFound, "source_or_policy_not_found");
        var policy = current.Policy;

        var prior = await _attempts.ListForSourceAsync(tenantId, request.SourceEntryId, cancellationToken);
        if (prior.Any(a => string.Equals(a.AttemptKey, request.AttemptKey, StringComparison.Ordinal)))
            return Result(RawSourceFetchStatus.IdempotencyConflict, "attempt_key_conflict");
        var attemptNumber = 1 + prior.Reverse().TakeWhile(a => a.Outcome == FetchAttemptOutcome.Failed).Count();
        var requestedUri = Sanitize(request.RequestedUri);

        var outcome = await RetrieveAsync(policy, request, cancellationToken);
        var now = _time.GetUtcNow();

        if (outcome.Bytes is null)
            return await RecordFailureAsync(tenantId, request, requestedUri, outcome, attemptNumber, now, actorSubject, cancellationToken);

        var digest = RawContentDigest.Compute(outcome.Bytes);
        if (request.PinnedContentSha256 is not null)
        {
            var pinned = RawContentDigest.Verify(outcome.Bytes, request.PinnedContentSha256);
            if (!pinned.Matches)
                return await RecordFailureAsync(
                    tenantId, request, requestedUri,
                    Failed(pinned.Reason ?? "digest_mismatch", FetchFailureClass.PolicyDenied, null, null),
                    attemptNumber, now, actorSubject, cancellationToken);
        }

        string? latestDigest = null;
        Guid? latestId = null;
        // Only a prior retrieval of the same source AND the same final URI may be deduplicated against.
        foreach (var success in prior.Where(a => a.Outcome == FetchAttemptOutcome.Succeeded && a.RawRetrievalId is not null)
                     .Reverse().Take(MaxDedupLookback))
        {
            var existing = await _raw.GetAsync(tenantId, success.RawRetrievalId!.Value, cancellationToken);
            if (existing is not null && string.Equals(existing.FinalUri, outcome.FinalUri, StringComparison.Ordinal))
            {
                latestDigest = existing.ContentSha256;
                latestId = existing.Id;
                break;
            }
        }

        var dedup = RawRetrievalDedup.Decide(latestDigest, digest);
        if (dedup.Action == RawRetrievalDedupAction.Refused)
            return await RecordFailureAsync(
                tenantId, request, requestedUri,
                Failed(dedup.Reason ?? "dedup_refused", FetchFailureClass.PolicyDenied, null, null),
                attemptNumber, now, actorSubject, cancellationToken);

        Guid rawId;
        var status = RawSourceFetchStatus.Duplicate;
        if (dedup.Action == RawRetrievalDedupAction.Duplicate)
        {
            rawId = latestId!.Value;
        }
        else
        {
            var stored = await _raw.RecordAsync(
                tenantId,
                new RawRetrievalInput(request.SourceEntryId, request.AttemptKey, outcome.FinalUri!, now.UtcDateTime, outcome.Bytes),
                actorSubject, cancellationToken);
            if (stored.Outcome == RawRetrievalRecordOutcome.SourceNotFound)
                return Result(RawSourceFetchStatus.SourceNotFound, "source_or_policy_not_found");
            if (stored.Outcome == RawRetrievalRecordOutcome.IdempotencyConflict)
                return Result(RawSourceFetchStatus.IdempotencyConflict, "attempt_key_conflict");
            rawId = stored.Record!.Id;
            status = RawSourceFetchStatus.Stored;
        }

        var recorded = await _attempts.RecordAsync(
            tenantId,
            new FetchAttemptInput(
                request.SourceEntryId, request.AttemptKey, requestedUri, now.UtcDateTime,
                FetchAttemptOutcome.Succeeded, null, null, rawId),
            actorSubject, cancellationToken);
        if (recorded.Outcome == FetchAttemptRecordOutcome.IdempotencyConflict)
            return Result(RawSourceFetchStatus.IdempotencyConflict, "attempt_key_conflict");
        return new RawSourceFetchResult(status, null, rawId, digest, null);
    }

    private async Task<RawSourceFetchResult> RecordFailureAsync(
        Guid tenantId, RawSourceFetchRequest request, string requestedUri, Outcome outcome,
        int attemptNumber, DateTimeOffset now, string actor, CancellationToken ct)
    {
        var recorded = await _attempts.RecordAsync(
            tenantId,
            new FetchAttemptInput(
                request.SourceEntryId, request.AttemptKey, requestedUri, now.UtcDateTime,
                FetchAttemptOutcome.Failed, outcome.FailureClass, outcome.HttpStatus, null),
            actor, ct);
        if (recorded.Outcome == FetchAttemptRecordOutcome.IdempotencyConflict)
            return Result(RawSourceFetchStatus.IdempotencyConflict, "attempt_key_conflict");

        FetchRetryObservation? observation = outcome.HttpStatus is { } status
            ? FetchRetryObservationFactory.FromHttpResponse(attemptNumber, status, outcome.RetryAfter, now)
            : outcome.FailureClass switch
            {
                FetchFailureClass.Network => FetchRetryObservationFactory.FromTransportFailure(attemptNumber, FetchRetryFailure.Network),
                FetchFailureClass.Timeout => FetchRetryObservationFactory.FromTransportFailure(attemptNumber, FetchRetryFailure.Timeout),
                FetchFailureClass.SizeLimit => FetchRetryObservationFactory.FromTransportFailure(attemptNumber, FetchRetryFailure.SizeLimit),
                _ => FetchRetryObservationFactory.FromTransportFailure(attemptNumber, FetchRetryFailure.PolicyDenied),
            };
        var due = FetchRetryDueTime.From(now, FetchRetryPolicy.Decide(SourceFetchRetrySchedule.Default, observation));
        return new RawSourceFetchResult(
            due is null ? RawSourceFetchStatus.Refused : RawSourceFetchStatus.RetryLater,
            outcome.Reason, null, null, due);
    }

    private async Task<Outcome> RetrieveAsync(
        SourceFetchPolicy policy, RawSourceFetchRequest request, CancellationToken cancellationToken)
    {
        var limits = request.Limits;
        // Validate limits before any DNS or network I/O, and only then install the deadline.
        var admitted = FetchResponseLimitGuard.CheckProgress(limits, null, 0, TimeSpan.Zero);
        if (!admitted.Admitted) return Failed(admitted.Reason ?? "invalid_limits", FetchFailureClass.PolicyDenied, null, null);
        var started = _time.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limits.MaxTotalDuration);
        var token = deadline.Token;

        try
        {
            var client = _http;
            var hops = new List<FetchHop>();
            var uri = request.RequestedUri;
            while (true)
            {
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
                    return Failed("invalid_uri", FetchFailureClass.PolicyDenied, null, null);

                // Host first: never resolve a name the policy does not allow.
                var hostCheck = FetchDestinationGuard.EvaluateHop(
                    policy, request.Expectation, new FetchHop(uri, [IPAddress.Parse("93.184.216.34")]));
                if (!hostCheck.Admitted && hostCheck.Reason != "forbidden_address")
                    return Failed(hostCheck.Reason ?? "invalid_uri", FetchFailureClass.PolicyDenied, null, null);

                IReadOnlyList<IPAddress> addresses;
                try { addresses = await _resolver.ResolveAsync(parsed.IdnHost, token); }
                catch (Exception e) when (e is SocketException or InvalidOperationException)
                { return Failed("unresolved_destination", FetchFailureClass.Network, null, null); }

                hops.Add(new FetchHop(uri, addresses));
                var chain = FetchDestinationGuard.EvaluateRedirectChain(policy, request.Expectation, hops);
                if (!chain.Admitted)
                    return Failed(chain.Reason ?? "invalid_uri", FetchFailureClass.PolicyDenied, null, null);

                using var message = new HttpRequestMessage(HttpMethod.Get, chain.FinalUri);
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);

                if (response.RequestMessage?.RequestUri?.AbsoluteUri is { } sent
                    && !string.Equals(sent, chain.FinalUri, StringComparison.Ordinal))
                    return Failed("unexpected_redirect", FetchFailureClass.PolicyDenied, null, null);

                var code = (int)response.StatusCode;
                if (code is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location;
                    if (location is null) return Failed("redirect_without_location", FetchFailureClass.PolicyDenied, null, null);
                    uri = (location.IsAbsoluteUri ? location : new Uri(parsed, location)).AbsoluteUri;
                    continue;
                }
                if (code is < 200 or >= 300)
                {
                    var retryAfter = response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
                    return new Outcome(null, null, "http_status", FetchFailureClass.HttpStatus, code, retryAfter);
                }

                return await ReadBodyAsync(response, chain.FinalUri!, limits!, started, token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("fetch_timeout", FetchFailureClass.Timeout, null, null);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return Failed("network_error", FetchFailureClass.Network, null, null);
        }
    }

    private async Task<Outcome> ReadBodyAsync(
        HttpResponseMessage response, string finalUri, SourceFetchLimits limits, long started, CancellationToken token)
    {
        var declared = response.Content.Headers.ContentLength;
        var early = FetchResponseLimitGuard.CheckProgress(limits, declared, 0, _time.GetElapsedTime(started));
        if (!early.Admitted) return LimitFailure(early);

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunk];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, token);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            var progress = FetchResponseLimitGuard.CheckProgress(limits, declared, buffer.Length, _time.GetElapsedTime(started));
            if (!progress.Admitted) return LimitFailure(progress);
        }

        var final = FetchResponseLimitGuard.EvaluateCompleted(
            limits, new FetchResponseObservation(declared, buffer.Length, _time.GetElapsedTime(started), true));
        if (!final.Admitted) return LimitFailure(final);
        if (buffer.Length > TenantRawRetrievalLimits.MaxRawBytes)
            return Failed("response_too_large", FetchFailureClass.SizeLimit, null, null);
        return new Outcome(buffer.ToArray(), finalUri, null, null, null, null);
    }

    private static Outcome LimitFailure(FetchLimitDecision decision)
    {
        var cls = FetchResponseLimitGuard.LedgerFailureClass(decision) switch
        {
            "size_limit" => FetchFailureClass.SizeLimit,
            "timeout" => FetchFailureClass.Timeout,
            _ => FetchFailureClass.PolicyDenied,
        };
        return Failed(decision.Reason ?? "policy_denied", cls, null, null);
    }

    private static Outcome Failed(string reason, FetchFailureClass cls, int? status, string? retryAfter)
        => new(null, null, reason, cls, status, retryAfter);

    private static RawSourceFetchResult Result(RawSourceFetchStatus status, string reason)
        => new(status, reason, null, null, null);

    /// <summary>Attempt records keep scheme, host, port and path only: no userinfo, query or fragment.</summary>
    public static string Sanitize(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host)) return "invalid:uri";
        var text = $"{parsed.Scheme}://{parsed.IdnHost}{(parsed.IsDefaultPort ? "" : ":" + parsed.Port)}{parsed.AbsolutePath}";
        return text.Length <= TenantFetchAttemptLimits.MaxRequestedUriLength ? text : text[..TenantFetchAttemptLimits.MaxRequestedUriLength];
    }

    private sealed record Outcome(
        byte[]? Bytes, string? FinalUri, string? Reason, FetchFailureClass? FailureClass, int? HttpStatus, string? RetryAfter);
}

/// <summary>
/// Connect-time guard against DNS rebinding: the address actually dialled is resolved and
/// vetted here, so a name that passed the earlier check cannot move to a denied range.
/// </summary>
public static class GuardedConnect
{
    public static async Task<Stream> ConnectAsync(
        DnsEndPoint endpoint, IHostAddressResolver resolver, CancellationToken cancellationToken)
    {
        var addresses = await resolver.ResolveAsync(endpoint.Host, cancellationToken);
        if (addresses.Count == 0 || addresses.Any(FetchDestinationGuard.IsForbiddenAddress))
            throw new HttpRequestException("forbidden_address");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(addresses[0], endpoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

public sealed class SystemHostAddressResolver : IHostAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        => await Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>Singleton holder so DI can hand the shared, guarded client to the scoped fetcher.</summary>
public sealed record RawSourceHttpClient(HttpClient Client);
