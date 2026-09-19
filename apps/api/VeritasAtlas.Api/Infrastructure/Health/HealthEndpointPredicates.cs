using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VeritasAtlas.Api.Infrastructure.Health;

/// <summary>
/// Liveness means only that the HTTP pipeline responds. Readiness includes
/// configured downstream probes such as the PostgreSQL health check.
/// </summary>
public static class HealthEndpointPredicates
{
    public static bool IsLiveness(HealthCheckRegistration _) => false;

    public static bool IsReadiness(HealthCheckRegistration registration)
        => registration.Tags.Contains("ready");
}
