using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

public static class EvidenceDetailLimits
{
    public const int MaxRefLength = 512;
    public const int MaxChunks = 200;
    public const int MaxChunkLength = 4000;
}

public sealed record EvidenceDetailChunk(int Ordinal, string ElementId, string Text);

/// <summary>
/// Provenance, honestly labelled freshness and bounded verified chunk text of one verified evidence
/// item. <see cref="ChunksTruncated"/> is true when more chunks exist than
/// <see cref="EvidenceDetailLimits.MaxChunks"/> or a chunk text was cut to
/// <see cref="EvidenceDetailLimits.MaxChunkLength"/> characters.
/// </summary>
public sealed record EvidenceDetail(
    string EvidenceRef,
    string SourceUri,
    Guid SourceEntryId,
    string Jurisdiction,
    string SourceType,
    string AttestationState,
    DateTime RetrievedAtUtc,
    FreshnessState Freshness,
    bool FreshnessEligible,
    string? FreshnessReason,
    string? FreshnessEvaluatedAt,
    string? ParserName,
    string? ParserVersion,
    IReadOnlyList<EvidenceDetailChunk> Chunks,
    bool ChunksTruncated);

public enum EvidenceDetailOutcome
{
    Ok,

    /// <summary>Unknown, unverified and foreign-tenant references are indistinguishable.</summary>
    NotFound,

    /// <summary>The reference is empty, over-long or contains control characters.</summary>
    InvalidRequest
}

public sealed record EvidenceDetailResult(EvidenceDetailOutcome Outcome, EvidenceDetail? Detail);

/// <summary>VER-P0-17 (#342). Only verified evidence of the caller's tenant is readable; freshness is re-evaluated now.</summary>
public interface IEvidenceDetailService
{
    Task<EvidenceDetailResult> GetAsync(Guid tenantId, string evidenceRef, CancellationToken cancellationToken);
}
