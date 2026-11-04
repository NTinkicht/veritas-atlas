namespace VeritasAtlas.Domain.Provenance;

public enum GuardOutcome { Apply, Replay, Deny }

public static class GuardDenials
{
    public const string OperationIdMissing = "operation_id_missing";
    public const string StaleVersion = "stale_version";
    public const string OperationConflict = "operation_conflict";
    public const string ApprovalDigestMismatch = "approval_digest_mismatch";
}

/// <summary>
/// Facts about one transition attempt, supplied from trusted server-side state. <c>Prior*</c> describe the last
/// operation already committed against the revision (null when none).
/// </summary>
public sealed record GuardRequest(
    long ExpectedVersion, long CurrentVersion, string? OperationId, LifecycleAction Action,
    string? PriorOperationId, LifecycleAction? PriorAction,
    string? ApprovedDigest, string? CurrentDigest);

public sealed record GuardResult(GuardOutcome Outcome, string? DenialCode);

/// <summary>
/// Pure deterministic, fail-closed optimistic-version, idempotency and approval-binding rules for publication
/// transitions (VER-P0-16 slice 3). Complements <see cref="PublicationLifecycle"/>, which decides legality only.
/// </summary>
public static class PublicationVersionGuard
{
    public static GuardResult Evaluate(GuardRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (string.IsNullOrWhiteSpace(r.OperationId)) return Deny(GuardDenials.OperationIdMissing);

        if (r.PriorOperationId is not null && string.Equals(r.PriorOperationId, r.OperationId, StringComparison.Ordinal))
        {
            // Exact retry of the committed operation: same action and the version it produced.
            if (r.PriorAction == r.Action && r.ExpectedVersion >= 0 && r.ExpectedVersion < long.MaxValue && r.ExpectedVersion + 1 == r.CurrentVersion)
                return new(GuardOutcome.Replay, null);
            return Deny(GuardDenials.OperationConflict);
        }

        if (r.ExpectedVersion < 0 || r.CurrentVersion < 0 || r.ExpectedVersion != r.CurrentVersion) return Deny(GuardDenials.StaleVersion);

        if (r.Action == LifecycleAction.Publish &&
            (string.IsNullOrWhiteSpace(r.ApprovedDigest) || string.IsNullOrWhiteSpace(r.CurrentDigest) ||
             !string.Equals(r.ApprovedDigest, r.CurrentDigest, StringComparison.Ordinal)))
            return Deny(GuardDenials.ApprovalDigestMismatch);

        return new(GuardOutcome.Apply, null);
    }

    private static GuardResult Deny(string code) => new(GuardOutcome.Deny, code);
}
