namespace VeritasAtlas.Infrastructure.Persistence;

/// <summary>
/// A subject's private saved evidence-search criteria (VER-P1-01 slice 1). Holds filter criteria only;
/// no results, no freshness state and no cursor are ever stored.
/// </summary>
public sealed class TenantSavedSearch
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;

    public Guid? SourceEntryId { get; set; }

    public string? Jurisdiction { get; set; }

    public string? SourceType { get; set; }

    public string? Freshness { get; set; }

    public string? ParserName { get; set; }

    public string? ParserVersion { get; set; }

    public string? SubjectTerm { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Current version, starting at 1. Every effective edit increments it by exactly one.</summary>
    public int Version { get; set; } = 1;
}

/// <summary>
/// Immutable snapshot of one saved-search version (VER-P1-01 slice 4). Criteria only, like the parent row.
/// Removed with its saved search.
/// </summary>
public sealed class TenantSavedSearchVersion
{
    public Guid TenantId { get; set; }

    public Guid SavedSearchId { get; set; }

    public int Version { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;

    public Guid? SourceEntryId { get; set; }

    public string? Jurisdiction { get; set; }

    public string? SourceType { get; set; }

    public string? Freshness { get; set; }

    public string? ParserName { get; set; }

    public string? ParserVersion { get; set; }

    public string? SubjectTerm { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
