using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VeritasAtlas.Api.Contracts.Auth;
using VeritasAtlas.Api.Infrastructure.Auth;

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

        // Locked accounts are refused even with the right password; unknown
        // usernames lock identically, so this is not an enumeration oracle.
        if (_attemptGuard.IsLockedOut(request.Username))
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
            _attemptGuard.RecordFailure(request.Username);
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
