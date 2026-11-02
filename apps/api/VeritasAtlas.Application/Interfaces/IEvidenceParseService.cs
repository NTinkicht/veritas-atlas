namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-07 slice 2 (#157): ties one tenant raw retrieval, its parse run and the chunks of
/// parser v1 together. A parse is recorded exactly once per raw retrieval and parser version;
/// a success is stored as <c>parsed_unverified</c> together with its chunks, a failure is
/// stored with its failure class and no chunks. Nothing here verifies evidence: chunks stay
/// unverified and are never answerable until attestation and classification proof are checked
/// by the separate verification step.
/// </summary>
public interface IEvidenceParseService
{
    Task<EvidenceParseResult> ParseAsync(
        Guid tenantId, Guid rawRetrievalId, string actorSubject, CancellationToken cancellationToken);
}

public enum EvidenceParseOutcome
{
    /// <summary>A new run (and its chunks) was written; the parse succeeded.</summary>
    ParsedUnverified,

    /// <summary>A new failed run was written; no chunks exist.</summary>
    Failed,

    /// <summary>This retrieval was already parsed by this parser version: the original run.</summary>
    Replayed,

    /// <summary>The raw retrieval is unknown or belongs to another tenant.</summary>
    RetrievalNotFound
}

/// <param name="FailureReason">Parser reason for a new failure; not stored, the class is.</param>
public sealed record EvidenceParseResult(
    EvidenceParseOutcome Outcome,
    TenantParseRunModel? Run,
    string? FailureReason,
    int ChunkCount);
