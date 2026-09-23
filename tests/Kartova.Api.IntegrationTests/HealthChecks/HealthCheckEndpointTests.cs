using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Audit.Infrastructure;
using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Kartova.Api.IntegrationTests.HealthChecks;

[TestClass]
public class HealthCheckEndpointTests : KeycloakContainerTestBase
{
    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        try
        {
            await PostgresTestBootstrap.SeedRolesAndSchemaAsync(Containers.Postgres.GetConnectionString());
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateObject)
        {
            // Seeded by another test class sharing this assembly's Postgres container.
        }

        var migratorConn = PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.MigratorRole);
        await PostgresTestBootstrap.RunMigrationsAsync<CatalogDbContext>(migratorConn, o => new CatalogDbContext(o));
        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(migratorConn, o => new OrganizationDbContext(o));
        await PostgresTestBootstrap.RunMigrationsAsync<AuditDbContext>(migratorConn, o => new AuditDbContext(o));

        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), Containers.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{Containers.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__BaseUrl", Containers.KeycloakBaseUrl);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__Realm", RealmSeedConstants.RealmName);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientId", RealmSeedConstants.AdminClientId);
        Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientSecret", RealmSeedConstants.AdminClientSecret);

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
    }

    [TestCleanup]
    public void DisposeAsync() => _app?.Dispose();

    [TestMethod]
    public async Task Live_returns_200_and_only_the_self_entry()
    {
        var resp = await _app!.CreateClient().GetAsync("/health/live");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var entries = await EntryKeysAsync(resp);
        CollectionAssert.AreEquivalent(new[] { "self" }, entries);
    }

    [TestMethod]
    public async Task Ready_returns_postgres_and_keycloak_entries_and_is_healthy()
    {
        var resp = await _app!.CreateClient().GetAsync("/health/ready");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var doc = await ParseAsync(resp);
        Assert.AreEqual("Healthy", doc.RootElement.GetProperty("status").GetString());
        CollectionAssert.AreEquivalent(new[] { "postgres", "keycloak" }, await EntryKeysAsync(resp, doc));
    }

    [TestMethod]
    public async Task Startup_returns_postgres_keycloak_and_migrations_entries_and_is_healthy()
    {
        var resp = await _app!.CreateClient().GetAsync("/health/startup");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var doc = await ParseAsync(resp);
        Assert.AreEqual("Healthy", doc.RootElement.GetProperty("status").GetString());
        CollectionAssert.AreEquivalent(new[] { "postgres", "keycloak", "migrations" }, await EntryKeysAsync(resp, doc));
    }

    [TestMethod]
    public async Task Detailed_returns_401_when_anonymous()
    {
        var resp = await _app!.CreateClient().GetAsync("/health/detailed");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Detailed_returns_403_for_a_non_platform_admin_user()
    {
        var token = await GetRealTokenAsync("admin@orga.kartova.local", "dev_password_12");
        var client = _app!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/health/detailed");
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Detailed_returns_200_with_exception_field_for_a_platform_admin_user()
    {
        var token = await GetRealTokenAsync("platform-admin@kartova.local", "dev_password_12");
        var client = _app!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/health/detailed");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var doc = await ParseAsync(resp);
        var self = doc.RootElement.GetProperty("entries").GetProperty("self");
        Assert.IsTrue(self.TryGetProperty("exception", out _), "detailed writer must expose an exception field (even if null)");
        CollectionAssert.AreEquivalent(new[] { "self", "postgres", "keycloak", "migrations" }, await EntryKeysAsync(resp, doc));
    }

    private static async Task<string> GetRealTokenAsync(string username, string password)
    {
        using var oidc = new HttpClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "kartova-api",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        });
        var tokenResp = await oidc.PostAsync($"{Containers.KeycloakAuthority}/protocol/openid-connect/token", form);
        tokenResp.EnsureSuccessStatusCode();
        var payload = await tokenResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return payload!["access_token"].ToString()!;
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage resp)
        => JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

    private static async Task<string[]> EntryKeysAsync(HttpResponseMessage resp, JsonDocument? parsed = null)
    {
        var doc = parsed ?? await ParseAsync(resp);
        return doc.RootElement.GetProperty("entries").EnumerateObject().Select(p => p.Name).ToArray();
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
