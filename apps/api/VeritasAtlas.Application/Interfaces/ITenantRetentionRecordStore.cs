namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-18 slice 3 (#300): append-only, versioned retention record of one raw retrieval (legal hold flag
/// and an optional retain-until instant). The latest version wins. A retain-until may never move earlier
/// than the previous record, the database refuses UPDATE/DELETE/TRUNCATE, and every appended version writes
/// its <c>retention.recorded</c> audit event in the same transaction. Nothing here deletes data.
/// </summary>
public interface ITenantRetentionRecordStore
{
    Task<TenantRetentionRecordModel?> GetLatestAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken);

    /// <summary>Every recorded version of one retrieval in this tenant, oldest first (empty when none).</summary>
    Task<IReadOnlyList<TenantRetentionRecordModel>> ListAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken);

    /// <summary>Appends the next version; an identical request to the latest version is a no-change replay.</summary>
    Task<RetentionRecordAppendResult> AppendAsync(
        Guid tenantId, Guid rawRetrievalId, bool legalHold, DateTime? retainUntilUtc, string actorSubject,
        CancellationToken cancellationToken);
}

public sealed record TenantRetentionRecordModel(
    int Version, bool LegalHold, DateTime? RetainUntilUtc, string ActorSubject, DateTime CreatedAtUtc);

public enum RetentionRecordAppendOutcome { Appended, NoChange, RetrievalNotFound, WindowShortened, Conflict }

public sealed record RetentionRecordAppendResult(RetentionRecordAppendOutcome Outcome, TenantRetentionRecordModel? Record);
