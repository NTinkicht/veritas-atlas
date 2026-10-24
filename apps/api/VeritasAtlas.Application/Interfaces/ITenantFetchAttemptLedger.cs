namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-03 slice 3 (#89): append-only, tenant-scoped ledger of fetch
/// attempts. A failed attempt records why it failed; a successful attempt
/// points at the raw retrieval it produced, which must belong to the same
/// tenant and the same source. Rows can never be updated or deleted.
/// </summary>
public interface ITenantFetchAttemptLedger
{
    Task<FetchAttemptRecordResult> RecordAsync(
        Guid tenantId, FetchAttemptInput input, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantFetchAttemptModel>> ListForSourceAsync(
        Guid tenantId, Guid sourceEntryId, CancellationToken cancellationToken);
}

public enum FetchAttemptOutcome
{
    Succeeded,
    Failed
}

public enum FetchFailureClass
{
    Network,
    Timeout,
    HttpStatus,
    SizeLimit,
    PolicyDenied
}

/// <summary>
/// <see cref="RawRetrievalId"/> is required for a success and forbidden for a
/// failure; <see cref="FailureClass"/> is required for a failure and forbidden
/// for a success; <see cref="HttpStatus"/> is required for
/// <see cref="FetchFailureClass.HttpStatus"/> and otherwise optional.
/// </summary>
public sealed record FetchAttemptInput(
    Guid SourceEntryId,
    string AttemptKey,
    string RequestedUri,
    DateTime AttemptedAtUtc,
    FetchAttemptOutcome Outcome,
    FetchFailureClass? FailureClass,
    int? HttpStatus,
    Guid? RawRetrievalId);

public sealed record TenantFetchAttemptModel(
    Guid Id,
    Guid SourceEntryId,
    string AttemptKey,
    string RequestedUri,
    DateTime AttemptedAtUtc,
    FetchAttemptOutcome Outcome,
    FetchFailureClass? FailureClass,
    int? HttpStatus,
    Guid? RawRetrievalId,
    DateTime CreatedAtUtc);

public enum FetchAttemptRecordOutcome
{
    /// <summary>A new append-only row was written.</summary>
    Recorded,

    /// <summary>The same attempt key with the same content: the original row.</summary>
    Replayed,

    /// <summary>The attempt key is already bound to a different attempt.</summary>
    IdempotencyConflict,

    /// <summary>The source entry is unknown or belongs to another tenant.</summary>
    SourceNotFound,

    /// <summary>The raw retrieval is unknown, another tenant's, or another source's.</summary>
    RetrievalNotFound
}

public sealed record FetchAttemptRecordResult(
    FetchAttemptRecordOutcome Outcome, TenantFetchAttemptModel? Attempt);

public static class TenantFetchAttemptLimits
{
    public const int MaxAttemptKeyLength = 200;
    public const int MaxRequestedUriLength = 2048;
}
