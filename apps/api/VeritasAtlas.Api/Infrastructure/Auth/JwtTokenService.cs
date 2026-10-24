using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace VeritasAtlas.Api.Infrastructure.Auth;

public sealed class JwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public JwtTokenService(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(
        string username, string role, Guid? tenantId = null)
    {
        var secret = RuntimeSecurity.ResolveJwtSecret(
            _configuration,
            _environment.EnvironmentName);
        var issuer = RuntimeSecurity.ResolveJwtIssuer(_configuration);
        var audience = RuntimeSecurity.ResolveJwtAudience(_configuration);
        var expiresAtUtc = DateTime.UtcNow.Add(
            RuntimeSecurity.ResolveJwtLifetime(_configuration));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new("preferred_username", username),
            new("role", role),
            // VER-P0-02 (#79): unique, unguessable token id so logout can
            // revoke exactly this token server-side.
            new(JwtRegisteredClaimNames.Jti, Convert.ToHexStringLower(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
        };

        // VER-P0-03 (#82): the organization tenant is a signed claim; it is
        // omitted (never defaulted) when the identity has no tenant.
        if (tenantId is { } tenant && tenant != Guid.Empty)
        {
            claims.Add(new Claim(TenantClaim.Type, tenant.ToString("D")));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
