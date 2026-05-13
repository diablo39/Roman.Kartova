using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Kartova.Api.IntegrationTests;

[TestClass]
public class AuthSmokeTests : KeycloakContainerTestBase
{
    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(Containers.Postgres.GetConnectionString());

        // Env vars must be set BEFORE the WebApplicationFactory boots the host.
        // Program.Main reads ConnectionStrings:* and Authentication:* before the
        // WithWebHostBuilder callback runs, so env vars are the only vehicle that reaches that code.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), Containers.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{Containers.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
        });

        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            PostgresTestBootstrap.ConnectionStringFor(Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.MigratorRole),
            opts => new OrganizationDbContext(opts));
        await SeedOrgA();
    }

    [TestCleanup]
    public void DisposeAsync()
    {
        _app?.Dispose();
    }

    [TestMethod]
    public async Task Full_KeyCloak_realm_issues_token_and_API_accepts_it()
    {
        using var oidc = new HttpClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "kartova-api",
            ["username"] = "admin@orga.kartova.local",
            ["password"] = "dev_pass",
            ["scope"] = "openid",
        });
        var tokenResp = await oidc.PostAsync($"{Containers.KeycloakAuthority}/protocol/openid-connect/token", form);
        tokenResp.EnsureSuccessStatusCode();
        var payload = await tokenResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var accessToken = payload!["access_token"].ToString()!;

        var client = _app!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    private async Task SeedOrgA()
    {
        var bypassConnectionString = PostgresTestBootstrap.ConnectionStringFor(
            Containers.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole);
        await using var conn = new NpgsqlConnection(bypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO organizations (id, tenant_id, name, created_at) VALUES ($1, $2, 'Org A', now())";
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
