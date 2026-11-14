using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace VeritasAtlas.Api.Infrastructure.Auth;

/// <summary>
/// VER-P0-01 (#264): the only roles the product trusts. A principal carrying no role claim, or any role claim
/// outside this set (even alongside a known one), is not trusted and is refused at the authorization gate,
/// before any controller or store is reached.
/// </summary>
public static class KnownRoles
{
    public static readonly IReadOnlyList<string> All = ["operator", "analyst", "reviewer", "publisher", "admin"];

    public static bool IsKnown(string? role) =>
        !string.IsNullOrWhiteSpace(role) && All.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>Fail closed: at least one role claim and every distinct role claim value is known.</summary>
    public static bool PrincipalHasOnlyKnownRoles(ClaimsPrincipal user)
    {
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role")).Select(c => c.Value).ToList();
        return roles.Count > 0 && roles.All(IsKnown);
    }

    /// <summary>Authenticated and trusted-role-only. Used as both the default and the fallback policy.</summary>
    public static AuthorizationPolicy BuildPolicy() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => PrincipalHasOnlyKnownRoles(context.User))
            .Build();
}
