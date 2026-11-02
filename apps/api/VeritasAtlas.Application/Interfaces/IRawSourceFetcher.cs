using System.Net;
using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-06 (#156): internal service that performs one bounded, policy-checked HTTP
/// retrieval of a tenant's registered source and records the outcome. It produces an
/// unparsed raw record only; it never mints an <c>ev1:</c> reference, parses, schedules
/// or exposes an endpoint, so a retrieved record stays ineligible for citation.
/// </summary>
public interface IRawSourceFetcher
{
    Task<RawSourceFetchResult> FetchAsync(
        Guid tenantId, RawSourceFetchRequest request, string actorSubject, CancellationToken cancellationToken);
}

/// <summary>Resolves a host name to addresses. Abstracted so tests never touch DNS.</summary>
public interface IHostAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

/// <param name="AttemptKey">Unique per attempt; the idempotency key of the attempt record.</param>
/// <param name="PinnedContentSha256">When set, the retrieved bytes must hash to exactly this digest.</param>
public sealed record RawSourceFetchRequest(
    Guid SourceEntryId,
    string AttemptKey,
    string RequestedUri,
    FetchExpectation Expectation,
    SourceFetchLimits Limits,
    string? PinnedContentSha256 = null);

public enum RawSourceFetchStatus
{
    /// <summary>New bytes persisted as an immutable raw record.</summary>
    Stored,
    /// <summary>Identical bytes to the latest record: no new record, attempt recorded.</summary>
    Duplicate,
    /// <summary>Policy, limit, digest or transport refusal. No raw record was created.</summary>
    Refused,
    /// <summary>Publisher asked to retry later; <see cref="RawSourceFetchResult.RetryDueAtUtc"/> is set.</summary>
    RetryLater,
    /// <summary>Unknown source, no current policy, or another tenant's source.</summary>
    SourceNotFound,
    /// <summary>The attempt key was already used for a different attempt.</summary>
    IdempotencyConflict
}

public sealed record RawSourceFetchResult(
    RawSourceFetchStatus Status,
    string? Reason,
    Guid? RawRetrievalId,
    string? ContentSha256,
    DateTimeOffset? RetryDueAtUtc);
