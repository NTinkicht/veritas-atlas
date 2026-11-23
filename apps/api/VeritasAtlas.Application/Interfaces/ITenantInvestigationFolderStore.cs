namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P1-01 slice 3: a subject's private folder for a tenant investigation. A subject files an
/// investigation into at most one folder; other subjects never see it.
/// </summary>
public static class TenantInvestigationFolderLimits
{
    public const int MaxNameLength = 60;
}

public interface ITenantInvestigationFolderStore
{
    Task<FolderSetResult> SetAsync(
        Guid tenantId, Guid investigationId, string name, string subject, CancellationToken cancellationToken);

    Task<FolderClearOutcome> ClearAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken);

    Task<FolderGetResult> GetAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken);
}

public sealed record TenantInvestigationFolderModel(
    Guid InvestigationId, string Name, string Subject, DateTime UpdatedAtUtc);

public enum FolderSetOutcome
{
    Created,
    Changed,
    Replayed,
    NotFound,
    InvestigationClosed
}

public sealed record FolderSetResult(FolderSetOutcome Outcome, TenantInvestigationFolderModel? Folder);

public enum FolderClearOutcome
{
    Cleared,
    NotFiled,
    NotFound,
    InvestigationClosed
}

/// <summary>Folder is null when the investigation is unknown (<see cref="InvestigationFound"/> false) or not filed.</summary>
public sealed record FolderGetResult(bool InvestigationFound, TenantInvestigationFolderModel? Folder);

public static class TenantInvestigationFolderRules
{
    /// <summary>Trims, then accepts 1..60 chars of letters, digits, single inner spaces, '_', '.', '-'.</summary>
    public static string? Normalize(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > TenantInvestigationFolderLimits.MaxNameLength) return null;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var ascii = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
            if (ascii) continue;
            if (c is '_' or '.' or '-') continue;
            if (c == ' ' && name[i - 1] != ' ') continue;
            return null;
        }

        return name;
    }
}
