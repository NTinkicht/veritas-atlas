using VeritasAtlas.Domain.Provenance;

namespace VeritasAtlas.Application.Interfaces;

public static class EvidenceSearchLimits
{
    public const int MaxQueryLength = 256;
    public const int MaxFilterLength = 128;
    public const int MaxCursorLength = 1024;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
}

/// <summary>
/// One tenant-scoped full-text search request over verified evidence (VER-P0-09, #159).
/// <see cref="Freshness"/> restricts results to evidence whose server-side re-evaluation yields
/// that state; null returns every state with its honest label. <see cref="ParserName"/> and
/// <see cref="ParserVersion"/> are the provenance filters: they match the stored parse run behind the
/// verification exactly (ordinal, case-sensitive), so evidence without a parse run never matches.
/// </summary>
public sealed record EvidenceSearchRequest(
    string Query,
    Guid? SourceEntryId,
    string? Jurisdiction,
    string? SourceType,
    DateTime? RetrievedFromUtc,
    DateTime? RetrievedToUtc,
    FreshnessState? Freshness,
    int PageSize,
    string? Cursor,
    string? ParserName = null,
    string? ParserVersion = null);

public sealed record EvidenceSearchHit(
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
    string? ParserName = null,
    string? ParserVersion = null);

public enum EvidenceSearchOutcome
{
    Ok,

    /// <summary>The query, a filter, the page size or the cursor is malformed (uniform for a tampered or foreign cursor).</summary>
    InvalidRequest
}

public sealed record EvidenceSearchPage(
    EvidenceSearchOutcome Outcome,
    IReadOnlyList<EvidenceSearchHit> Hits,
    string? NextCursor);

/// <summary>
/// VER-P0-09 (#159). Only verified evidence of the caller's tenant is searchable; freshness is
/// re-evaluated with a fresh server-side instant for every hit on every page, before any freshness
/// filter. No total count is ever returned.
/// </summary>
public interface IEvidenceSearchService
{
    Task<EvidenceSearchPage> SearchAsync(Guid tenantId, EvidenceSearchRequest request, CancellationToken cancellationToken);
}
