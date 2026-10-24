using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace VeritasAtlas.Api.Infrastructure.Auth;

/// <summary>
/// Single-operator staging login; not a substitute for production SSO/MFA.
/// Credentials must be injected by Render and never stored in source control.
/// </summary>
public sealed class ProductionBootstrapAuthenticator
{
    private readonly string? _username;
    private readonly string? _password;
    private readonly Guid? _tenantId;

    public ProductionBootstrapAuthenticator(IConfiguration configuration)
    {
        _username = configuration["Auth:Bootstrap:Username"];
        _password = configuration["Auth:Bootstrap:Password"];
        _tenantId = TenantClaim.TryParse(configuration[TenantIdKey], out var tenantId)
            ? tenantId
            : null;
    }

    /// <summary>VER-P0-03 (#82): optional organization tenant of the bootstrap
    /// identity. When absent the token carries no tenant claim and every
    /// tenant-scoped endpoint denies; there is never a default tenant.</summary>
    public const string TenantIdKey = "Auth:Bootstrap:TenantId";

    public static void ValidateConfiguration(IConfiguration configuration, string? environmentName)
    {
        var configuredTenant = configuration[TenantIdKey];
        if (configuredTenant is not null && !TenantClaim.TryParse(configuredTenant, out _))
        {
            throw new InvalidOperationException(
                "Auth:Bootstrap:TenantId must be a non-empty UUID when configured.");
        }
        if (RuntimeSecurity.IsDevelopment(environmentName)) return;
        var username = configuration["Auth:Bootstrap:Username"];
        var password = configuration["Auth:Bootstrap:Password"];
        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
            || password.Length < 20)
        {
            throw new InvalidOperationException(
                "Non-development staging requires an Auth:Bootstrap:Username " +
                "and a unique Auth:Bootstrap:Password of at least 20 characters.");
        }
        if (DevUserStore.IsFixtureUsername(username)
            || string.Equals(password, "password123", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Development fixture credentials are forbidden outside Development.");
        }
    }

    public DevUserRecord? Validate(string username, string password)
    {
        if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password)) return null;
        if (username is null || password is null) return null;
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(_password));
        var samePassword = CryptographicOperations.FixedTimeEquals(supplied, expected);
        return samePassword && string.Equals(username, _username, StringComparison.Ordinal)
            ? new DevUserRecord(_username, "", "admin", _tenantId)
            : null;
    }
}
