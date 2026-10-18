using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace VeritasAtlas.Api.Infrastructure.Auth;

public static class RuntimeSecurity
{
    public const string DevelopmentJwtSecret =
        "veritas-atlas-dev-secret-key-change-in-production-123456";

    // HS256 needs a key of at least 256 bits.
    public const int MinimumJwtSecretBytes = 32;
    public const int DefaultJwtLifetimeMinutes = 480;
    public const int MaximumJwtLifetimeMinutes = 1440;
    public const string DefaultJwtIssuer = "VeritasAtlas";
    public const string DefaultJwtAudience = "VeritasAtlasUsers";

    private static readonly string[] DevelopmentCorsOrigins =
    [
        "http://localhost:5173",
        "https://localhost:5173",
        "http://127.0.0.1:5173",
        "https://127.0.0.1:5173"
    ];

    public static bool IsDevelopment(string? environmentName) =>
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);

    public static string ResolveJwtSecret(
        IConfiguration configuration,
        string? environmentName)
    {
        var configured = configuration["Auth:Jwt:Secret"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Encoding.UTF8.GetByteCount(configured) < MinimumJwtSecretBytes)
            {
                throw new InvalidOperationException(
                    $"Auth:Jwt:Secret must contain at least {MinimumJwtSecretBytes} bytes.");
            }

            if (!IsDevelopment(environmentName)
                && string.Equals(configured, DevelopmentJwtSecret, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The development JWT secret is forbidden outside Development.");
            }

            return configured;
        }

        if (IsDevelopment(environmentName))
        {
            return DevelopmentJwtSecret;
        }

        throw new InvalidOperationException(
            "Auth:Jwt:Secret must be configured outside Development.");
    }

    public static string ResolveJwtIssuer(IConfiguration configuration) =>
        configuration["Auth:Jwt:Issuer"] ?? DefaultJwtIssuer;

    public static string ResolveJwtAudience(IConfiguration configuration) =>
        configuration["Auth:Jwt:Audience"] ?? DefaultJwtAudience;

    public static TimeSpan ResolveJwtLifetime(IConfiguration configuration)
    {
        var configured = configuration["Auth:Jwt:ExpiresMinutes"];
        if (configured is null)
        {
            return TimeSpan.FromMinutes(DefaultJwtLifetimeMinutes);
        }

        // An explicit but malformed lifetime is a configuration error, not a
        // reason to fall back silently to the default.
        if (!int.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var minutes)
            || minutes < 1
            || minutes > MaximumJwtLifetimeMinutes)
        {
            throw new InvalidOperationException(
                $"Auth:Jwt:ExpiresMinutes must be an integer from 1 to {MaximumJwtLifetimeMinutes}.");
        }

        return TimeSpan.FromMinutes(minutes);
    }

    public static TokenValidationParameters CreateTokenValidationParameters(
        IConfiguration configuration,
        string? environmentName)
    {
        var secret = ResolveJwtSecret(configuration, environmentName);
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = ResolveJwtIssuer(configuration),
            ValidateAudience = true,
            ValidAudience = ResolveJwtAudience(configuration),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    public static string[] ResolveCorsOrigins(
        IConfiguration configuration,
        string? environmentName)
    {
        var configured = configuration
            .GetSection("Cors:AllowedOrigins")
            .GetChildren()
            .Select(item => item.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (configured.Length > 0)
        {
            return configured;
        }

        return IsDevelopment(environmentName)
            ? DevelopmentCorsOrigins.ToArray()
            : [];
    }
}
