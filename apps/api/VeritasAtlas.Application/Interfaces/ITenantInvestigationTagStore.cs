namespace VeritasAtlas.Application.Interfaces;

public static class TenantInvestigationTagLimits
{
    public const int MaxTagLength = 40;
    public const int MaxPerSubjectPerInvestigation = 20;
}

/// <summary>
/// VER-P1-01 slice 2: private per-subject organizing tags on a tenant investigation. A tag is a
/// lowercase label owned by the caller; it is never visible to another subject, never changes the
/// investigation, its evidence or any report, and every read and write is scoped to the caller
/// tenant and subject. Another tenant's investigation is indistinguishable from an unknown one.
/// </summary>
public interface ITenantInvestigationTagStore
{
    /// <summary>Idempotent: adding an already present tag replays the original row.</summary>
    Task<TagAddResult> AddAsync(
        Guid tenantId, Guid investigationId, string tag, string subject, CancellationToken cancellationToken);

    /// <summary>Removes only the caller's own tag; removing an absent tag is a no-op.</summary>
    Task<TagRemoveOutcome> RemoveAsync(
        Guid tenantId, Guid investigationId, string tag, string subject, CancellationToken cancellationToken);

    /// <summary>The caller's own tags; null when the investigation does not exist in the tenant.</summary>
    Task<IReadOnlyList<TenantInvestigationTagModel>?> ListAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken);
}

public sealed record TenantInvestigationTagModel(
    Guid InvestigationId, string Tag, string Subject, DateTime CreatedAtUtc);

public enum TagAddOutcome
{
    Added,
    Replayed,
    NotFound,
    InvestigationClosed,
    LimitReached
}

public sealed record TagAddResult(TagAddOutcome Outcome, TenantInvestigationTagModel? Tag);

public enum TagRemoveOutcome
{
    Removed,
    NotTagged,
    NotFound,
    InvestigationClosed
}

public static class TenantInvestigationTagRules
{
    /// <summary>Lowercase ASCII letters and digits, then letters, digits, underscore, dot or hyphen.</summary>
    public static bool IsValid(string tag)
    {
        if (tag.Length is < 1 or > TenantInvestigationTagLimits.MaxTagLength) return false;
        for (var i = 0; i < tag.Length; i++)
        {
            var c = tag[i];
            var alnum = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!(alnum || (i > 0 && c is '_' or '.' or '-'))) return false;
        }

        return true;
    }
}
