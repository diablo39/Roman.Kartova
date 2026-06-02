using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kartova.Api.IntegrationTests;

[TestClass]
public class CorsTests : KeycloakContainerTestBase
{
    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public void InitializeAsync()
    {
        // Env vars must be set BEFORE the WebApplicationFactory boots the host.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), Containers.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{Containers.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");

        // Slice 9 / H8: AddKeycloakAdminClient.ValidateOnStart rejects the
        // appsettings placeholder "OVERRIDE_VIA_ENV". Wire the four
        // KartovaIdentity__Keycloak__* env vars from the live container so the
        // host boots. Realm-seed literals live in RealmSeedConstants so a
        // future rename of kartova-realm.json only touches one site.
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__BaseUrl", Containers.KeycloakBaseUrl);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__Realm", RealmSeedConstants.RealmName);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientId", RealmSeedConstants.AdminClientId);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientSecret", RealmSeedConstants.AdminClientSecret);

        // CORS allowlist — set before WAF boots so the policy builder sees the value.
        Environment.SetEnvironmentVariable($"{CorsConfigKeys.AllowedOrigins.Replace(":", "__")}__0", "http://localhost:5173");

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
        });
    }

    [TestCleanup]
    public void DisposeAsync()
    {
        _app?.Dispose();
    }

    [TestMethod]
    public async Task Preflight_FromConfiguredOrigin_AllowsRequest()
    {
        var client = _app!.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
        req.Headers.Add("Origin", "http://localhost:5173");
        req.Headers.Add("Access-Control-Request-Method", "GET");

        var resp = await client.SendAsync(req);

        Assert.IsTrue(resp.Headers.Contains("Access-Control-Allow-Origin"));
        var origins = resp.Headers.GetValues("Access-Control-Allow-Origin").ToList();
        Assert.AreEqual(1, origins.Count);
        Assert.AreEqual("http://localhost:5173", origins[0]);
    }

    [TestMethod]
    public async Task Preflight_FromUnknownOrigin_DoesNotEchoOrigin()
    {
        var client = _app!.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/version");
        req.Headers.Add("Origin", "https://evil.example");
        req.Headers.Add("Access-Control-Request-Method", "GET");

        var resp = await client.SendAsync(req);

        Assert.IsFalse(
            resp.Headers.Contains("Access-Control-Allow-Origin"),
            "the API must not echo origins outside the configured allowlist.");
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
