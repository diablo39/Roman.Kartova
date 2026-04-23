using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Kartova.Api.IntegrationTests;

public class AuthSmokeTests : IClassFixture<KeycloakContainerFixture>, IAsyncLifetime
{
    private readonly KeycloakContainerFixture _fx;
    private WebApplicationFactory<Program>? _app;

    public AuthSmokeTests(KeycloakContainerFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await SeedPostgres();

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, c) =>
            {
                c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Kartova"] = AppConnectionString("kartova_app"),
                    ["ConnectionStrings:KartovaBypass"] = AppConnectionString("kartova_bypass_rls"),
                    ["Authentication:Authority"] = _fx.KeycloakAuthority,
                    ["Authentication:MetadataAddress"] = $"{_fx.KeycloakAuthority}/.well-known/openid-configuration",
                    ["Authentication:Audience"] = "kartova-api",
                    ["Authentication:RequireHttpsMetadata"] = "false",
                });
            });
        });

        await RunMigrationsAsync();
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

    private string AppConnectionString(string user) => new NpgsqlConnectionStringBuilder(_fx.Postgres.GetConnectionString())
    {
        Username = user,
        Password = user == "kartova_bypass_rls" ? "dev_only" : "dev",
    }.ToString();

    private async Task SeedPostgres()
    {
        await using var conn = new NpgsqlConnection(_fx.Postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE ROLE migrator WITH LOGIN PASSWORD 'dev' CREATEDB;
            CREATE ROLE kartova_app WITH LOGIN PASSWORD 'dev';
            CREATE ROLE kartova_bypass_rls WITH LOGIN PASSWORD 'dev_only' BYPASSRLS;
            GRANT CONNECT ON DATABASE kartova TO kartova_app, kartova_bypass_rls;
            ALTER SCHEMA public OWNER TO migrator;
            GRANT USAGE, CREATE ON SCHEMA public TO kartova_app, kartova_bypass_rls;
            GRANT CREATE ON DATABASE kartova TO kartova_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_app, kartova_bypass_rls;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RunMigrationsAsync()
    {
        // Use a dedicated migrator-role DbContext; the default DI registration in the API is tenant-scoped.
        var optsBuilder = new DbContextOptionsBuilder<Kartova.Organization.Infrastructure.OrganizationDbContext>();
        var migratorCs = new NpgsqlConnectionStringBuilder(_fx.Postgres.GetConnectionString())
        {
            Username = "migrator",
            Password = "dev",
        }.ToString();
        optsBuilder.UseNpgsql(migratorCs);
        await using var db = new Kartova.Organization.Infrastructure.OrganizationDbContext(optsBuilder.Options);
        await db.Database.MigrateAsync();
    }

    private async Task SeedOrgA()
    {
        await using var conn = new NpgsqlConnection(AppConnectionString("kartova_bypass_rls"));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO organizations (id, tenant_id, name, created_at) VALUES ($1, $2, 'Org A', now())";
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        await cmd.ExecuteNonQueryAsync();
    }
}
