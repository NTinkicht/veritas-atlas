using System.Security.Claims;

namespace VeritasAtlas.Api.Infrastructure.Auth;

/// <summary>
/// The single role allowed to write the tenant's trusted source registry: source
/// registration (VER-P0-04, #125) and fetch policy revisions (#123, decision B).
/// Both write paths use this one fail-closed rule.
/// </summary>
public static class TenantAdminRole
{
    public const string Name = "admin";

    /// <summary>
    /// Fail closed: the token's role claims (issued tokens carry the same role
    /// as both <see cref="ClaimTypes.Role"/> and <c>role</c>) must name exactly
    /// one role, <c>admin</c> (case-insensitive). No role, another role or two
    /// different roles are refused.
    /// </summary>
    public static bool IsSatisfiedBy(ClaimsPrincipal user)
    {
        var roles = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roles.Count == 1
            && string.Equals(roles[0], Name, StringComparison.OrdinalIgnoreCase);
    }
}
