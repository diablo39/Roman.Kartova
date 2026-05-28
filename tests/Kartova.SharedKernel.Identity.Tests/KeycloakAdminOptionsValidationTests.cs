using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Identity.Tests;

[TestClass]
public sealed class KeycloakAdminOptionsValidationTests
{
    private static IConfiguration BuildConfig(string adminClientSecret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KartovaIdentity:Keycloak:BaseUrl"] = "http://x",
                ["KartovaIdentity:Keycloak:Realm"] = "x",
                ["KartovaIdentity:Keycloak:AdminClientId"] = "x",
                ["KartovaIdentity:Keycloak:AdminClientSecret"] = adminClientSecret,
                ["KartovaIdentity:Keycloak:FrontendBaseUrl"] = "http://x",
            })
            .Build();

    [TestMethod]
    public void ValidateOnStart_rejects_placeholder_AdminClientSecret()
    {
        var services = new ServiceCollection();
        services.AddKeycloakAdminClient(BuildConfig("OVERRIDE_VIA_ENV"));

        using var sp = services.BuildServiceProvider();
        var ex = Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value);
        StringAssert.Contains(ex.Message, "OVERRIDE_VIA_ENV");
    }

    [TestMethod]
    public void ValidateOnStart_rejects_empty_AdminClientSecret()
    {
        var services = new ServiceCollection();
        services.AddKeycloakAdminClient(BuildConfig(""));

        using var sp = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value);
    }

    [TestMethod]
    public void ValidateOnStart_passes_when_AdminClientSecret_is_real_value()
    {
        var services = new ServiceCollection();
        services.AddKeycloakAdminClient(BuildConfig("real-secret-value"));

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
        Assert.AreEqual("real-secret-value", opts.AdminClientSecret);
    }
}
