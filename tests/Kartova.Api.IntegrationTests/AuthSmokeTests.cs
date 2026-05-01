using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Kartova.Api.IntegrationTests;

[Collection(KeycloakTestCollection.Name)]
public class AuthSmokeTests : IAsyncLifetime
{
    private readonly KeycloakContainerFixture _fx;
    private WebApplicationFactory<Program>? _app;

    public AuthSmokeTests(KeycloakContainerFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(_fx.Postgres.GetConnectionString());

        // Env vars must be set BEFORE the WebApplicationFactory boots the host.
        // Program.Main reads ConnectionStrings:* and Authentication:* before the
        // WithWebHostBuilder callback runs, so env vars are the only vehicle that reaches that code.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(_fx.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(_fx.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), _fx.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{_fx.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
        });

        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            PostgresTestBootstrap.ConnectionStringFor(_fx.Postgres.GetConnectionString(), PostgresTestBootstrap.MigratorRole),
            opts => new OrganizationDbContext(opts));
        await SeedOrgA();
    }

    public Task DisposeAsync()
    {
        _app?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
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
        var tokenResp = await oidc.PostAsync($"{_fx.KeycloakAuthority}/protocol/openid-connect/token", form);
        tokenResp.EnsureSuccessStatusCode();
        var payload = await tokenResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var accessToken = payload!["access_token"].ToString()!;

        var client = _app!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.GetAsync("/api/v1/organizations/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task SeedOrgA()
    {
        var bypassConnectionString = PostgresTestBootstrap.ConnectionStringFor(
            _fx.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole);
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
