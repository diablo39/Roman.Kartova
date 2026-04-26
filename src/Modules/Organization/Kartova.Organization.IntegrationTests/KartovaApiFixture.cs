using System.Diagnostics.CodeAnalysis;
using Kartova.Api;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

[ExcludeFromCodeCoverage]
public sealed class KartovaApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public TestJwtSigner Signer { get; } = new();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(_pg.GetConnectionString());
        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            MigratorConnectionString,
            opts => new OrganizationDbContext(opts));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }

    public string MainConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.AppRole);

    public string BypassConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.BypassRole);

    public string MigratorConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.MigratorRole);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Env vars must be set BEFORE Program.Main reads configuration; double-underscore maps to ':'.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}", MainConnectionString);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}", BypassConnectionString);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), TestJwtSigner.Issuer);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), TestJwtSigner.Audience);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.UseTestJwtSigner(Signer);
        });
    }

    public async Task<Guid> SeedOrganizationAsync(Guid tenantId, string name)
    {
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO organizations (id, tenant_id, name, created_at)
            VALUES ($1, $2, $3, now())
            ON CONFLICT (id) DO UPDATE
                SET name = EXCLUDED.name,
                    tenant_id = EXCLUDED.tenant_id
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(name);
        var id = (Guid)(await cmd.ExecuteScalarAsync())!;
        return id;
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
