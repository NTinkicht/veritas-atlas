using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VeritasAtlas.Api.Contracts.Auth;
using VeritasAtlas.Api.Infrastructure.Auth;
using VeritasAtlas.Application.Interfaces;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly DevUserStore _devUserStore;
    private readonly ProductionBootstrapAuthenticator _bootstrapAuthenticator;
    private readonly JwtTokenService _jwtTokenService;
    private readonly AuthRequestContext _authRequestContext;
    private readonly IHostEnvironment _environment;
    private readonly LoginAttemptGuard _attemptGuard;

    public AuthController(
        DevUserStore devUserStore,
        ProductionBootstrapAuthenticator bootstrapAuthenticator,
        JwtTokenService jwtTokenService,
        AuthRequestContext authRequestContext,
        IHostEnvironment environment,
        LoginAttemptGuard attemptGuard)
    {
        _devUserStore = devUserStore;
        _bootstrapAuthenticator = bootstrapAuthenticator;
        _jwtTokenService = jwtTokenService;
        _authRequestContext = authRequestContext;
        _environment = environment;
        _attemptGuard = attemptGuard;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimiting.PolicyName)]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest? request)
    {
        // Malformed or oversized credentials get the same generic rejection
        // as wrong ones, before any credential check (never a 500).
        if (request is null
            || !LoginAttemptGuard.IsWellFormed(request.Username, request.Password))
        {
            return InvalidCredentials();
        }

        // Reserve the attempt atomically before any credential check, so
        // concurrent requests for one account cannot exceed the threshold.
        // Locked accounts are refused even with the right password; unknown
        // usernames lock identically, so this is not an enumeration oracle.
        if (!_attemptGuard.TryReserveAttempt(request.Username))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new AuthErrorResponse(
                "login_temporarily_locked",
                "Too many failed sign-in attempts. Try again later.",
                DateTime.UtcNow));
        }

        // No development fixture is accepted outside Development.
        var user = _environment.IsDevelopment()
            ? _devUserStore.Validate(request.Username, request.Password)
            : _bootstrapAuthenticator.Validate(request.Username, request.Password);
        if (user is null)
        {
            // The reserved attempt already counts as this failure.
            return InvalidCredentials();
        }

        _attemptGuard.RecordSuccess(request.Username);

        var token = _jwtTokenService.CreateToken(user.Username, user.Role);

        return Ok(new LoginResponse(
            token.Token,
            "Bearer",
            token.ExpiresAtUtc,
            user.Username,
            user.Role));
    }

    private UnauthorizedObjectResult InvalidCredentials() =>
        Unauthorized(new AuthErrorResponse(
            "invalid_credentials",
            "The supplied username or password is invalid.",
            DateTime.UtcNow));

    /// <summary>
    /// VER-P0-02 (#79): revokes the caller's own bearer server-side. The token
    /// to revoke is taken only from the authenticated principal, never from the
    /// request, so a caller cannot revoke another user's token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(
        [FromServices] IAccessTokenRevocationStore revocationStore,
        CancellationToken cancellationToken)
    {
        var jti = AccessTokenRevocation.GetJti(User);
        var expiresAtUtc = ExpiresAtUtc(User);
        if (jti is null || expiresAtUtc is null)
        {
            // Unreachable for tokens issued by this API (OnTokenValidated
            // already requires a jti and validation requires exp).
            return Unauthorized();
        }

        await revocationStore.RevokeAsync(jti, expiresAtUtc.Value, cancellationToken);
        return NoContent();
    }

    private static DateTime? ExpiresAtUtc(System.Security.Claims.ClaimsPrincipal principal)
    {
        var exp = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Exp)?.Value;
        return long.TryParse(exp, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        return Ok(new
        {
            user = User.Identity?.Name,
            role = _authRequestContext.GetRole(HttpContext),
            isAuthenticated = User.Identity?.IsAuthenticated == true,
            claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList()
        });
    }
}
