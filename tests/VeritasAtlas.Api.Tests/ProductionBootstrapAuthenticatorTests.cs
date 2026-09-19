using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VeritasAtlas.Api.Infrastructure.Auth;

namespace VeritasAtlas.Api.Tests;

[TestClass]
public sealed class ProductionBootstrapAuthenticatorTests
{
    [TestMethod]
    public void StagingFailsClosedWithoutBootstrapSecret()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Auth:Bootstrap:Username"] = "stagingadmin"
        });
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ProductionBootstrapAuthenticator.ValidateConfiguration(configuration, "Production"));
    }

    [TestMethod]
    public void StagingRejectsWeakBootstrapSecret()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Auth:Bootstrap:Username"] = "stagingadmin",
            ["Auth:Bootstrap:Password"] = "password123"
        });
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ProductionBootstrapAuthenticator.ValidateConfiguration(configuration, "Production"));
    }

    [TestMethod]
    public void DevelopmentDoesNotRequireStagingSecret()
    {
        ProductionBootstrapAuthenticator.ValidateConfiguration(Config(), "Development");
    }

    [TestMethod]
    public void StagingAcceptsOnlyConfiguredStrongAdminIdentity()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Auth:Bootstrap:Username"] = "stagingadmin",
            ["Auth:Bootstrap:Password"] = "an-independent-staging-secret-1234"
        });
        ProductionBootstrapAuthenticator.ValidateConfiguration(configuration, "Production");
        var auth = new ProductionBootstrapAuthenticator(configuration);
        var user = auth.Validate("stagingadmin", "an-independent-staging-secret-1234");
        Assert.IsNotNull(user);
        Assert.AreEqual("stagingadmin", user.Username);
        Assert.AreEqual("admin", user.Role);
        Assert.IsNull(auth.Validate("stagingadmin", "wrong"));
        Assert.IsNull(auth.Validate("admin1", "an-independent-staging-secret-1234"));
    }

    private static IConfiguration Config(IDictionary<string, string?>? values = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
}
