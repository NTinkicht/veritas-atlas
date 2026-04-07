using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(string username, string role)
    {
        var secret = _configuration["Auth:Jwt:Secret"] ?? "veritas-atlas-dev-secret-key-change-in-production-123456";
        var issuer = _configuration["Auth:Jwt:Issuer"] ?? "VeritasAtlas";
        var audience = _configuration["Auth:Jwt:Audience"] ?? "VeritasAtlasUsers";
        var expiresMinutes = int.TryParse(_configuration["Auth:Jwt:ExpiresMinutes"], out var parsedMinutes) ? parsedMinutes : 480;

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new("preferred_username", username),
            new("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}