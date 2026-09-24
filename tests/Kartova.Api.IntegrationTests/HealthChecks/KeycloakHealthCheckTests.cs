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
        using var built = BuildCheck(Containers.KeycloakBaseUrl, RealmSeedConstants.RealmName);

        var result = await built.Check.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Healthy, result.Status);
    }

    [TestMethod]
    public async Task Unhealthy_when_discovery_endpoint_unreachable()
    {
        using var built = BuildCheck("http://127.0.0.1:1", "kartova");

        var result = await built.Check.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Unhealthy, result.Status);
    }

    [TestMethod]
    public async Task Unhealthy_when_discovery_endpoint_returns_non_success_status()
    {
        // Real KeyCloak container, but a realm that was never imported — its discovery
        // endpoint returns 404. Exercises the `!response.IsSuccessStatusCode` branch of
        // KeycloakHealthCheck.CheckHealthAsync, distinct from the connection-refused path above.
        using var built = BuildCheck(Containers.KeycloakBaseUrl, "a-realm-name-that-does-not-exist");

        var result = await built.Check.CheckHealthAsync(new HealthCheckContext());

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

    private static BuiltCheck BuildCheck(string baseUrl, string realm)
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
        var check = new KeycloakHealthCheck(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<KeycloakAdminOptions>>());
        return new BuiltCheck(check, provider);
    }

    /// <summary>
    /// Pairs a <see cref="KeycloakHealthCheck"/> with the <see cref="ServiceProvider"/> that
    /// owns its dependencies, so callers can dispose the provider (and its HttpClientFactory)
    /// deterministically via <c>using var built = BuildCheck(...)</c> instead of leaking it.
    /// </summary>
    private readonly record struct BuiltCheck(KeycloakHealthCheck Check, ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }
}
