using Kartova.Api.HealthChecks;
using Kartova.SharedKernel.Identity;
using Kartova.Testing.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Kartova.Api.IntegrationTests.HealthChecks;

[TestClass]
public class KeycloakHealthCheckTests : KeycloakContainerTestBase
{
    [TestMethod]
    public async Task Healthy_when_real_discovery_endpoint_responds()
    {
        var check = BuildCheck(Containers.KeycloakBaseUrl, RealmSeedConstants.RealmName);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Healthy, result.Status);
    }

    [TestMethod]
    public async Task Unhealthy_when_discovery_endpoint_unreachable()
    {
        var check = BuildCheck("http://127.0.0.1:1", "kartova");

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
    }

    [TestMethod]
    public async Task Unhealthy_result_never_contains_the_configured_admin_secret()
    {
        const string secret = "super-secret-value-should-never-leak";
        var services = new ServiceCollection();
        services.AddHttpClient(KeycloakHealthCheck.ClientName, http => http.Timeout = TimeSpan.FromSeconds(1));
        services.AddSingleton<IOptions<KeycloakAdminOptions>>(Options.Create(new KeycloakAdminOptions
        {
            BaseUrl = "http://127.0.0.1:1",
            Realm = "kartova",
            AdminClientId = "kartova-admin",
            AdminClientSecret = secret,
        }));
        using var provider = services.BuildServiceProvider();
        var check = new KeycloakHealthCheck(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<KeycloakAdminOptions>>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
        StringAssert.DoesNotMatch(result.Description ?? "", new System.Text.RegularExpressions.Regex(secret));
        Assert.IsFalse(result.Exception?.ToString().Contains(secret, StringComparison.Ordinal) ?? false);
    }

    private static KeycloakHealthCheck BuildCheck(string baseUrl, string realm)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(KeycloakHealthCheck.ClientName, http => http.Timeout = TimeSpan.FromSeconds(3));
        services.AddSingleton<IOptions<KeycloakAdminOptions>>(Options.Create(new KeycloakAdminOptions
        {
            BaseUrl = baseUrl,
            Realm = realm,
            AdminClientId = "kartova-admin",
            AdminClientSecret = "unused",
        }));
        var provider = services.BuildServiceProvider();
        return new KeycloakHealthCheck(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<KeycloakAdminOptions>>());
    }
}
