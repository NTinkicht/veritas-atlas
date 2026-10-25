namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-03 slice 2 (#87): immutable, tenant-scoped ledger of raw retrieved
/// bytes. The SHA-256 digest is computed server-side and re-checked by the
/// database; a row can never be updated or deleted, and no evidence reference
/// is minted before the versioned parser (VER-P0-07).
/// </summary>
public interface ITenantRawRetrievalLedger
{
    Task<RawRetrievalRecordResult> RecordAsync(
        Guid tenantId, RawRetrievalInput input, CancellationToken cancellationToken);

    Task<TenantRawRetrievalModel?> GetAsync(
        Guid tenantId, Guid id, CancellationToken cancellationToken);
}

public sealed record RawRetrievalInput(
    Guid SourceEntryId,
    string IdempotencyKey,
    string FinalUri,
    DateTime RetrievedAtUtc,
    byte[] RawBytes);

public sealed record TenantRawRetrievalModel(
    Guid Id,
    Guid SourceEntryId,
    string IdempotencyKey,
    string FinalUri,
    string ContentSha256,
    long ByteLength,
    DateTime RetrievedAtUtc,
    DateTime CreatedAtUtc);

public enum RawRetrievalRecordOutcome
{
    /// <summary>A new immutable row was written.</summary>
    Recorded,

    /// <summary>The same idempotency key with the same digest: the original row.</summary>
    Replayed,

    /// <summary>The idempotency key is already bound to different bytes.</summary>
    IdempotencyConflict,

    /// <summary>The source entry is unknown or belongs to another tenant.</summary>
    SourceNotFound
}

public sealed record RawRetrievalRecordResult(
    RawRetrievalRecordOutcome Outcome, TenantRawRetrievalModel? Record);

public static class TenantRawRetrievalLimits
{
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxFinalUriLength = 2048;
    public const int MaxRawBytes = 16 * 1024 * 1024;
}
