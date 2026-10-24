using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Infrastructure.Auth;

/// <summary>
/// VER-P0-02 (#79): rejects bearers that carry no <c>jti</c> or whose
/// <c>jti</c> was revoked by logout. Fails closed: if the revocation store
/// cannot be read, the token is rejected.
/// </summary>
public static class AccessTokenRevocation
{
    public const int MaximumJtiLength = 128;

    public static string? GetJti(ClaimsPrincipal? principal)
    {
        var jti = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        return string.IsNullOrWhiteSpace(jti) || jti.Length > MaximumJtiLength ? null : jti;
    }

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var jti = GetJti(context.Principal);
        if (jti is null)
        {
            context.Fail("The access token has no valid token identifier.");
            return;
        }

        bool revoked;
        try
        {
            var store = context.HttpContext.RequestServices
                .GetRequiredService<IAccessTokenRevocationStore>();
            revoked = await store.IsRevokedAsync(jti, context.HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AccessTokenRevocation))
                .LogError("Access token revocation check unavailable; rejecting the token ({ExceptionType}).",
                    exception.GetType().Name);
            context.Fail("The access token revocation state could not be verified.");
            return;
        }

        if (revoked)
        {
            context.Fail("The access token has been revoked.");
        }
    }
}
