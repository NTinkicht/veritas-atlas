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
    private readonly IHostEnvironment _environment;

    public AuthDiagnosticsController(
        AuthRequestContext authRequestContext,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _authRequestContext = authRequestContext;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult Public()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

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
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

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
