using System.Security.Claims;

namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class AuthRequestContext
{
    public string GetRole(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.Role)
            ?? context.User.FindFirstValue("role")
            ?? "anonymous";
    }

    public string GetUsername(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.Name)
            ?? context.User.FindFirstValue("preferred_username")
            ?? "anonymous";
    }

    public bool IsAuthenticated(HttpContext context)
    {
        return context.User.Identity?.IsAuthenticated == true;
    }
}