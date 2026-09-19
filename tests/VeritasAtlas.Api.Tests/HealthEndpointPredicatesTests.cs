using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Health;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class HealthEndpointPredicatesTests
{
    [TestMethod]
    public void LivenessDoesNotDependOnPostgreSql()
    {
        var database = Registration("postgresql", "ready");
        Assert.IsFalse(HealthEndpointPredicates.IsLiveness(database));
        Assert.IsTrue(HealthEndpointPredicates.IsReadiness(database));
    }

    [TestMethod]
    public void ReadinessExcludesUnrelatedChecks()
    {
        var unrelated = Registration("sample", "diagnostic");
        Assert.IsFalse(HealthEndpointPredicates.IsLiveness(unrelated));
        Assert.IsFalse(HealthEndpointPredicates.IsReadiness(unrelated));
    }

    private static HealthCheckRegistration Registration(string name, string tag)
        => new(name, new HealthyCheck(), HealthStatus.Unhealthy, new[] { tag });

    private sealed class HealthyCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(HealthCheckResult.Healthy());
    }
}
