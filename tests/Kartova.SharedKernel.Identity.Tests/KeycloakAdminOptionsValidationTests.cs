using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Identity.Tests;

[TestClass]
public sealed class KeycloakAdminOptionsValidationTests
{
    private static IConfiguration BuildConfig(
        string adminClientSecret,
        string frontendBaseUrl = "http://x") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KartovaIdentity:Keycloak:BaseUrl"] = "http://x",
                ["KartovaIdentity:Keycloak:Realm"] = "x",
                ["KartovaIdentity:Keycloak:AdminClientId"] = "x",
                ["KartovaIdentity:Keycloak:AdminClientSecret"] = adminClientSecret,
                ["KartovaIdentity:Keycloak:FrontendBaseUrl"] = frontendBaseUrl,
            })
            .Build();

    private static ServiceCollection BuildServices(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment(environmentName));
        return services;
    }

    [TestMethod]
    public void ValidateOnStart_rejects_placeholder_AdminClientSecret()
    {
        var services = BuildServices(Environments.Development);
        services.AddKeycloakAdminClient(BuildConfig("OVERRIDE_VIA_ENV"));

        using var sp = services.BuildServiceProvider();
        var ex = Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value);
        StringAssert.Contains(ex.Message, "OVERRIDE_VIA_ENV");
    }

    [TestMethod]
    public void ValidateOnStart_rejects_empty_AdminClientSecret()
    {
        var services = BuildServices(Environments.Development);
        services.AddKeycloakAdminClient(BuildConfig(""));

        using var sp = services.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value);
    }

    [TestMethod]
    public void ValidateOnStart_passes_when_AdminClientSecret_is_real_value()
    {
        var services = BuildServices(Environments.Development);
        services.AddKeycloakAdminClient(BuildConfig("real-secret-value"));

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
        Assert.AreEqual("real-secret-value", opts.AdminClientSecret);
    }

    [TestMethod]
    [DataRow("http://localhost:5173")]
    [DataRow("http://localhost")]
    [DataRow("https://localhost:5173")]
    [DataRow("HTTP://LOCALHOST")]
    public void ValidateOnStart_rejects_localhost_FrontendBaseUrl_outside_Development(string url)
    {
        var services = BuildServices(Environments.Production);
        services.AddKeycloakAdminClient(BuildConfig("real-secret-value", frontendBaseUrl: url));

        using var sp = services.BuildServiceProvider();
        var ex = Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value);
        StringAssert.Contains(ex.Message, "localhost");
        StringAssert.Contains(ex.Message, "Production");
    }

    [TestMethod]
    public void ValidateOnStart_allows_localhost_FrontendBaseUrl_in_Development()
    {
        var services = BuildServices(Environments.Development);
        services.AddKeycloakAdminClient(
            BuildConfig("real-secret-value", frontendBaseUrl: "http://localhost:5173"));

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
        Assert.AreEqual("http://localhost:5173", opts.FrontendBaseUrl);
    }

    [TestMethod]
    public void ValidateOnStart_allows_localhost_FrontendBaseUrl_in_Testing_env()
    {
        // Mirrors the Development-env passing-path test but with EnvironmentName = "Testing".
        // Justification: every integration test in the repo bootstraps via WebApplicationFactory<Program>
        // with UseEnvironment("Testing") (see tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs) and
        // inherits appsettings.json's FrontendBaseUrl = http://localhost:5173 default. Without this
        // allow-list extension, every Catalog / Organization / Api integration test would fail bootstrap
        // with OptionsValidationException at host startup.
        var services = BuildServices("Testing");
        services.AddKeycloakAdminClient(
            BuildConfig("real-secret-value", frontendBaseUrl: "http://localhost:5173"));

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
        Assert.AreEqual("http://localhost:5173", opts.FrontendBaseUrl);
    }

    [TestMethod]
    public void ValidateOnStart_allows_non_localhost_FrontendBaseUrl_in_Production()
    {
        var services = BuildServices(Environments.Production);
        services.AddKeycloakAdminClient(
            BuildConfig("real-secret-value", frontendBaseUrl: "https://app.kartova.example"));

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
        Assert.AreEqual("https://app.kartova.example", opts.FrontendBaseUrl);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Kartova.SharedKernel.Identity.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
