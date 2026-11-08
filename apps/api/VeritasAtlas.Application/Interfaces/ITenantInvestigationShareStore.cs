namespace VeritasAtlas.Application.Interfaces;

/// <summary>
/// VER-P0-10 slice 6: sharing of a tenant investigation. Only the investigation's creator may
/// grant, revoke and list; every other caller, another tenant and an unknown id are
/// indistinguishable (<c>NotFound</c>). A share never copies or mutates evidence.
/// </summary>
public interface ITenantInvestigationShareStore
{
    /// <summary>Idempotent; granting again after a revocation reactivates the grant.</summary>
    Task<ShareGrantResult> GrantAsync(
        Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken);

    /// <summary>Idempotent; revoking a grant that is not active is a no-op.</summary>
    Task<ShareRevokeOutcome> RevokeAsync(
        Guid tenantId, Guid investigationId, string owner, string grantee, CancellationToken cancellationToken);

    /// <summary>Active grants, oldest first; null when the caller is not the creator.</summary>
    Task<IReadOnlyList<TenantInvestigationShareModel>?> ListActiveAsync(
        Guid tenantId, Guid investigationId, string owner, CancellationToken cancellationToken);

    /// <summary>True only for the creator or a subject holding an active grant in the same tenant.</summary>
    Task<bool> IsAuthorizedAsync(
        Guid tenantId, Guid investigationId, string subject, CancellationToken cancellationToken);
}

public sealed record TenantInvestigationShareModel(
    Guid InvestigationId, string GranteeSubject, string GrantedBySubject, DateTime GrantedAtUtc);

public enum ShareGrantOutcome
{
    Granted,
    Replayed,
    NotFound,
    SelfGrant
}

public sealed record ShareGrantResult(ShareGrantOutcome Outcome, TenantInvestigationShareModel? Share);

public enum ShareRevokeOutcome
{
    Revoked,
    NotActive,
    NotFound
}

public static class TenantInvestigationShareLimits
{
    /// <summary>Keeps the audit entity id (investigation id, "/", grantee) within its column.</summary>
    public const int MaxGranteeLength = TenantAuditEventLimits.MaxEntityIdLength - 37;

    public static bool IsValidGrantee(string? grantee) =>
        !string.IsNullOrWhiteSpace(grantee)
        && grantee.Length <= MaxGranteeLength
        && grantee == grantee.Trim()
        && !grantee.Any(char.IsControl);
}
