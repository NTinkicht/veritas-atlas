using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-06 follow-up (#167): tenant-scoped store of the retrieval checkpoint of each source
/// queue. The advance rule is the pure <see cref="RetrievalCheckpointAdvance"/>; this store only
/// supplies its inputs from the database (the stored checkpoint, and whether the proposed raw
/// retrieval is persisted for the same tenant and source) and applies the decision atomically.
/// It never verifies an attestation, never sets an evidence reference and never mints <c>ev1:</c>.
/// </summary>
public interface ITenantRetrievalCheckpointStore
{
    /// <summary>The checkpoint of one source of the caller tenant, or <c>null</c> (none yet, unknown or another tenant's).</summary>
    Task<TenantRetrievalCheckpointModel?> GetAsync(Guid tenantId, Guid sourceEntryId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the checkpoint to <paramref name="sequence"/>, backed by the stored raw retrieval
    /// <paramref name="rawRetrievalId"/>, only if <see cref="RetrievalCheckpointAdvance"/> allows it.
    /// The checkpoint key is the raw retrieval's idempotency key.
    /// </summary>
    Task<RetrievalCheckpointAdvanceResult> AdvanceAsync(
        Guid tenantId, Guid sourceEntryId, long sequence, Guid rawRetrievalId, DateTime completedAtUtc,
        CancellationToken cancellationToken);
}

public sealed record TenantRetrievalCheckpointModel(
    Guid SourceEntryId, long Sequence, Guid RawRetrievalId, string Key, DateTime CompletedAtUtc);

public enum RetrievalCheckpointAdvanceOutcome
{
    Advanced,
    Replayed,

    /// <summary>The pure rule refused; see <see cref="RetrievalCheckpointAdvanceResult.Reason"/>.</summary>
    Refused,

    /// <summary>The raw retrieval is unknown, or belongs to another tenant or another source.</summary>
    RetrievalNotFound,

    /// <summary>A concurrent writer moved the checkpoint first; nothing was changed.</summary>
    ConcurrentUpdate
}

public sealed record RetrievalCheckpointAdvanceResult(
    RetrievalCheckpointAdvanceOutcome Outcome, string? Reason, TenantRetrievalCheckpointModel? Checkpoint);
