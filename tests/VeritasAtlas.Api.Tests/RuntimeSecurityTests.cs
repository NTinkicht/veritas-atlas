using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class RuntimeSecurityTests
{
    [TestMethod]
    public void ProductionWithoutJwtSecretFailsClosed()
    {
        var configuration = BuildConfiguration();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RuntimeSecurity.ResolveJwtSecret(configuration, "Production"));
    }

    [TestMethod]
    public void DevelopmentWithoutJwtSecretUsesDevelopmentFallback()
    {
        var configuration = BuildConfiguration();

        var secret = RuntimeSecurity.ResolveJwtSecret(configuration, "Development");

        Assert.AreEqual(RuntimeSecurity.DevelopmentJwtSecret, secret);
    }

    [TestMethod]
    public void ConfiguredJwtSecretIsUsedOutsideDevelopment()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Auth:Jwt:Secret"] = "production-secret-from-external-configuration"
        });

        var secret = RuntimeSecurity.ResolveJwtSecret(configuration, "Production");

        Assert.AreEqual("production-secret-from-external-configuration", secret);
    }

    [TestMethod]
    public void ProductionWithoutCorsConfigurationAllowsNoCrossOriginOrigins()
    {
        var configuration = BuildConfiguration();

        var origins = RuntimeSecurity.ResolveCorsOrigins(configuration, "Production");

        Assert.AreEqual(0, origins.Length);
    }

    [TestMethod]
    public void DevelopmentWithoutCorsConfigurationUsesLocalOrigins()
    {
        var configuration = BuildConfiguration();

        var origins = RuntimeSecurity.ResolveCorsOrigins(configuration, "Development");

        CollectionAssert.Contains(origins, "http://localhost:5173");
        CollectionAssert.Contains(origins, "https://localhost:5173");
    }

    [TestMethod]
    public void ConfiguredCorsOriginsOverrideDevelopmentDefaults()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://app.example.test",
            ["Cors:AllowedOrigins:1"] = "https://review.example.test"
        });

        var origins = RuntimeSecurity.ResolveCorsOrigins(configuration, "Production");

        CollectionAssert.AreEquivalent(
            new[] { "https://app.example.test", "https://review.example.test" },
            origins);
    }

    private static IConfiguration BuildConfiguration(
        IDictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }
}
