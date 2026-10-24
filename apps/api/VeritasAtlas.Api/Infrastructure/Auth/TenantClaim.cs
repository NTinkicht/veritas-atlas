using System.Security.Claims;

namespace VeritasAtlas.Api.Infrastructure.Auth;

/// <summary>
/// VER-P0-03 (#82, owner decision A in #80): the caller's organization tenant
/// is carried as a signed <c>tenant</c> claim. Resolution fails closed: a
/// missing, malformed, empty or ambiguous (repeated) claim yields no tenant,
/// and there is never a fallback tenant.
/// </summary>
public static class TenantClaim
{
    public const string Type = "tenant";

    public static bool TryParse(string? value, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        return value is not null
            && Guid.TryParseExact(value, "D", out tenantId)
            && tenantId != Guid.Empty;
    }

    public static Guid? GetTenantId(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var claims = principal.FindAll(Type).ToList();
        return claims.Count == 1 && TryParse(claims[0].Value, out var tenantId)
            ? tenantId
            : null;
    }
}
