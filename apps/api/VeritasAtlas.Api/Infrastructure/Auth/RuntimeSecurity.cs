using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace VeritasAtlas.Api.Infrastructure.Auth;

public static class RuntimeSecurity
{
    public const string DevelopmentJwtSecret =
        "veritas-atlas-dev-secret-key-change-in-production-123456";

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
            return configured;
        }

        if (IsDevelopment(environmentName))
        {
            return DevelopmentJwtSecret;
        }

        throw new InvalidOperationException(
            "Auth:Jwt:Secret must be configured outside Development.");
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
