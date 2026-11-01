namespace VeritasAtlas.Domain.Provenance;

/// <summary>
/// The last retrieval a source queue has fully completed: a strictly increasing sequence, the
/// idempotency key of the retrieval that completed it and when that retrieval finished.
/// </summary>
public sealed record RetrievalCheckpoint(long Sequence, string Key, DateTime CompletedAtUtc);

/// <summary>A proposed advance: the checkpoint to move to and whether its raw record is stored.</summary>
public sealed record RetrievalCheckpointProposal(
    long Sequence, string Key, DateTime CompletedAtUtc, bool RawRecordPersisted);

public enum RetrievalCheckpointAction
{
    /// <summary>Move the checkpoint to the proposed value.</summary>
    Advance,

    /// <summary>The proposed value is already the checkpoint: change nothing.</summary>
    Replay,

    /// <summary>The proposal is unsafe; the checkpoint must stay where it is.</summary>
    Refused
}

public sealed record RetrievalCheckpointDecision(RetrievalCheckpointAction Action, string? Reason)
{
    public static readonly RetrievalCheckpointDecision Advance = new(RetrievalCheckpointAction.Advance, null);
    public static readonly RetrievalCheckpointDecision Replay = new(RetrievalCheckpointAction.Replay, null);
    public static RetrievalCheckpointDecision Refuse(string reason) => new(RetrievalCheckpointAction.Refused, reason);
}

/// <summary>
/// VER-P0-06 slice 7 (#139): pure, deterministic, fail-closed rule for when a source retrieval
/// queue checkpoint may advance. A checkpoint never moves ahead of persistence (the raw bytes and
/// metadata must already be stored), never skips a sequence, never moves backwards and never
/// changes the key recorded for a sequence. Reasons, checked in this order: <c>invalid_proposed</c>
/// (null proposal, sequence below 1, key empty/whitespace/over 200 characters, non-UTC time),
/// <c>invalid_current</c> (the same validity rules applied to the stored checkpoint),
/// <c>raw_not_persisted</c>, <c>sequence_key_conflict</c>, <c>regression</c>, <c>sequence_gap</c>,
/// <c>time_regression</c>. No I/O, clock or network; it neither persists the checkpoint nor makes
/// any raw record answerable or mints an <c>ev1:</c> reference.
/// </summary>
public static class RetrievalCheckpointAdvance
{
    /// <summary>Matches <c>TenantRawRetrievalLimits.MaxIdempotencyKeyLength</c>.</summary>
    public const int MaxKeyLength = 200;

    public static RetrievalCheckpointDecision Decide(
        RetrievalCheckpoint? current, RetrievalCheckpointProposal? proposed)
    {
        if (proposed is null
            || !IsValid(proposed.Sequence, proposed.Key, proposed.CompletedAtUtc))
            return RetrievalCheckpointDecision.Refuse("invalid_proposed");
        if (current is not null && !IsValid(current.Sequence, current.Key, current.CompletedAtUtc))
            return RetrievalCheckpointDecision.Refuse("invalid_current");
        if (!proposed.RawRecordPersisted)
            return RetrievalCheckpointDecision.Refuse("raw_not_persisted");

        if (current is null)
            return proposed.Sequence == 1
                ? RetrievalCheckpointDecision.Advance
                : RetrievalCheckpointDecision.Refuse("sequence_gap");

        if (proposed.Sequence == current.Sequence)
        {
            return string.Equals(current.Key, proposed.Key, StringComparison.Ordinal)
                && current.CompletedAtUtc == proposed.CompletedAtUtc
                    ? RetrievalCheckpointDecision.Replay
                    : RetrievalCheckpointDecision.Refuse("sequence_key_conflict");
        }
        if (proposed.Sequence < current.Sequence)
            return RetrievalCheckpointDecision.Refuse("regression");
        // Both sequences are >= 1, so the subtraction cannot overflow.
        if (proposed.Sequence - current.Sequence > 1)
            return RetrievalCheckpointDecision.Refuse("sequence_gap");
        if (proposed.CompletedAtUtc < current.CompletedAtUtc)
            return RetrievalCheckpointDecision.Refuse("time_regression");
        return RetrievalCheckpointDecision.Advance;
    }

    private static bool IsValid(long sequence, string? key, DateTime completedAtUtc) =>
        sequence >= 1
        && !string.IsNullOrWhiteSpace(key)
        && key.Length <= MaxKeyLength
        && completedAtUtc.Kind == DateTimeKind.Utc;
}
