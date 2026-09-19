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

    public AuthController(
        DevUserStore devUserStore,
        ProductionBootstrapAuthenticator bootstrapAuthenticator,
        JwtTokenService jwtTokenService,
        AuthRequestContext authRequestContext,
        IHostEnvironment environment)
    {
        _devUserStore = devUserStore;
        _bootstrapAuthenticator = bootstrapAuthenticator;
        _jwtTokenService = jwtTokenService;
        _authRequestContext = authRequestContext;
        _environment = environment;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        // No development fixture is accepted outside Development.
        var user = _environment.IsDevelopment()
            ? _devUserStore.Validate(request.Username, request.Password)
            : _bootstrapAuthenticator.Validate(request.Username, request.Password);
        if (user is null)
        {
            return Unauthorized(new AuthErrorResponse(
                "invalid_credentials",
                "The supplied username or password is invalid.",
                DateTime.UtcNow));
        }

        var token = _jwtTokenService.CreateToken(user.Username, user.Role);

        return Ok(new LoginResponse(
            token.Token,
            "Bearer",
            token.ExpiresAtUtc,
            user.Username,
            user.Role));
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
