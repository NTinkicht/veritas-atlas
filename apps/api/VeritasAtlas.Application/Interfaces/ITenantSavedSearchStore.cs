namespace VeritasAtlas.Application.Interfaces;

public static class TenantSavedSearchLimits
{
    public const int MaxNameLength = 100;
    public const int MaxPerSubject = 25;
}

/// <summary>
/// The stored criteria of one evidence search (VER-P1-01 slice 1). Only the filter criteria are saved,
/// never results, freshness verdicts or a cursor: running a saved search goes through the normal
/// evidence search, which re-evaluates freshness at that request.
/// </summary>
public sealed record TenantSavedSearchDefinition(
    string Name,
    string Query,
    Guid? SourceEntryId,
    string? Jurisdiction,
    string? SourceType,
    string? Freshness,
    string? ParserName,
    string? ParserVersion,
    string? SubjectTerm);

public sealed record TenantSavedSearchModel(
    Guid Id, TenantSavedSearchDefinition Definition, DateTime CreatedAtUtc);

public enum SavedSearchCreateOutcome
{
    Created,
    DuplicateName,
    LimitReached
}

public sealed record SavedSearchCreateResult(SavedSearchCreateOutcome Outcome, TenantSavedSearchModel? SavedSearch);

/// <summary>
/// Private, per-subject saved evidence searches. Every read and write is scoped to the caller tenant
/// and subject; another subject's or tenant's saved search is indistinguishable from an unknown id.
/// </summary>
public interface ITenantSavedSearchStore
{
    Task<SavedSearchCreateResult> CreateAsync(
        Guid tenantId, string subject, TenantSavedSearchDefinition definition, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantSavedSearchModel>> ListAsync(
        Guid tenantId, string subject, CancellationToken cancellationToken);

    /// <summary>True when the caller's own saved search existed and was removed.</summary>
    Task<bool> DeleteAsync(Guid tenantId, string subject, Guid id, CancellationToken cancellationToken);
}
