using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Auth;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly DevUserStore _devUserStore;
    private readonly JwtTokenService _jwtTokenService;
    private readonly AuthRequestContext _authRequestContext;

    public AuthController(
        DevUserStore devUserStore,
        JwtTokenService jwtTokenService,
        AuthRequestContext authRequestContext)
    {
        _devUserStore = devUserStore;
        _jwtTokenService = jwtTokenService;
        _authRequestContext = authRequestContext;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        var user = _devUserStore.Validate(request.Username, request.Password);
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