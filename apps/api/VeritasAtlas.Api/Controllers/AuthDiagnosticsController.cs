using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/auth-diagnostics")]
public class AuthDiagnosticsController : ControllerBase
{
    private readonly AuthRequestContext _authRequestContext;
    private readonly IConfiguration _configuration;

    public AuthDiagnosticsController(
        AuthRequestContext authRequestContext,
        IConfiguration configuration)
    {
        _authRequestContext = authRequestContext;
        _configuration = configuration;
    }

    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult Public()
    {
        return Ok(new
        {
            Mode = "public",
            JwtIssuer = _configuration["Auth:Jwt:Issuer"] ?? "VeritasAtlas",
            JwtAudience = _configuration["Auth:Jwt:Audience"] ?? "VeritasAtlasUsers",
            TimestampUtc = DateTime.UtcNow
        });
    }

    [HttpGet("protected")]
    [Authorize]
    public IActionResult Protected()
    {
        return Ok(new
        {
            Mode = "protected",
            Username = _authRequestContext.GetUsername(HttpContext),
            Role = _authRequestContext.GetRole(HttpContext),
            IsAuthenticated = _authRequestContext.IsAuthenticated(HttpContext),
            Claims = User.Claims.Select(x => new { x.Type, x.Value }).ToList(),
            TimestampUtc = DateTime.UtcNow
        });
    }
}