namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-03 slice 4 (#91): append-only, tenant-scoped history of which parser
/// name/version processed which raw retrieval. A run is either a failure (with
/// its failure class) or <c>parsed_unverified</c>: no run ever carries or
/// verifies an evidence reference. Only the later versioned parser (VER-P0-07)
/// may populate or verify that slot, through its own migration. Rows can never
/// be updated or deleted.
/// </summary>
public interface ITenantParseRunLedger
{
    /// <summary>
    /// <paramref name="actorSubject"/> is the parse run's initiator (analyst or
    /// parser worker subject); a newly recorded run writes its
    /// <c>tenant_parse_run.recorded</c> audit event in the same transaction
    /// (#105). Replays and refusals write no event.
    /// </summary>
    Task<ParseRunRecordResult> RecordAsync(
        Guid tenantId, ParseRunInput input, string actorSubject, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantParseRunModel>> ListForRetrievalAsync(
        Guid tenantId, Guid rawRetrievalId, CancellationToken cancellationToken);
}

public enum ParseRunOutcome
{
    Failed,
    ParsedUnverified
}

public enum ParseFailureClass
{
    UnsupportedFormat,
    Malformed,
    Oversized,
    ParserError
}

/// <summary>
/// <see cref="FailureClass"/> is required for <see cref="ParseRunOutcome.Failed"/>
/// and forbidden for <see cref="ParseRunOutcome.ParsedUnverified"/>.
/// <see cref="ParserName"/> and <see cref="ParserVersion"/> are lowercase
/// identities: <c>[a-z0-9]</c> followed by <c>[a-z0-9._-]</c>.
/// </summary>
public sealed record ParseRunInput(
    Guid RawRetrievalId,
    string RunKey,
    string ParserName,
    string ParserVersion,
    DateTime StartedAtUtc,
    ParseRunOutcome Outcome,
    ParseFailureClass? FailureClass);

public sealed record TenantParseRunModel(
    Guid Id,
    Guid RawRetrievalId,
    string RunKey,
    string ParserName,
    string ParserVersion,
    DateTime StartedAtUtc,
    ParseRunOutcome Outcome,
    ParseFailureClass? FailureClass,
    DateTime CreatedAtUtc);

public enum ParseRunRecordOutcome
{
    /// <summary>A new append-only row was written.</summary>
    Recorded,

    /// <summary>The same run key with the same content: the original row.</summary>
    Replayed,

    /// <summary>The run key is already bound to a different run.</summary>
    IdempotencyConflict,

    /// <summary>The raw retrieval is unknown or belongs to another tenant.</summary>
    RetrievalNotFound
}

public sealed record ParseRunRecordResult(
    ParseRunRecordOutcome Outcome, TenantParseRunModel? Run);

public static class TenantParseRunLimits
{
    public const int MaxRunKeyLength = 200;
    public const int MaxParserIdentityLength = 64;
}
