using Kartova.Organization.Infrastructure;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

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
        await InitRolesAndSchemaAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }

    public string MainConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
    {
        Username = "kartova_app",
        Password = "dev",
    }.ToString();

    public string BypassConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
    {
        Username = "kartova_bypass_rls",
        Password = "dev_only",
    }.ToString();

    public string MigratorConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
    {
        Username = "migrator",
        Password = "dev",
    }.ToString();

    private async Task InitRolesAndSchemaAsync()
    {
        var cs = _pg.GetConnectionString();
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE ROLE migrator WITH LOGIN PASSWORD 'dev' CREATEDB;
            CREATE ROLE kartova_app WITH LOGIN PASSWORD 'dev';
            CREATE ROLE kartova_bypass_rls WITH LOGIN PASSWORD 'dev_only' BYPASSRLS;
            GRANT CONNECT ON DATABASE kartova TO kartova_app, kartova_bypass_rls;
            ALTER SCHEMA public OWNER TO migrator;
            GRANT USAGE, CREATE ON SCHEMA public TO kartova_app;
            GRANT USAGE, CREATE ON SCHEMA public TO kartova_bypass_rls;
            GRANT CREATE ON DATABASE kartova TO kartova_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kartova_app, kartova_bypass_rls;
            ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA public
                GRANT USAGE, SELECT ON SEQUENCES TO kartova_app, kartova_bypass_rls;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Set env vars BEFORE host/app builder reads configuration in Program.cs top-level code.
        // Double-underscore maps to ':' in ASP.NET config.
        Environment.SetEnvironmentVariable("ConnectionStrings__Kartova", MainConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__KartovaBypass", BypassConnectionString);
        Environment.SetEnvironmentVariable("Authentication__Authority", TestJwtSigner.Issuer);
        Environment.SetEnvironmentVariable("Authentication__Audience", TestJwtSigner.Audience);
        Environment.SetEnvironmentVariable("Authentication__RequireHttpsMetadata", "false");
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

    public async Task RunMigrationsAsync()
    {
        // Run migrations using a dedicated migrator-role DbContext (not the tenant-scoped one).
        var optsBuilder = new DbContextOptionsBuilder<OrganizationDbContext>();
        optsBuilder.UseNpgsql(MigratorConnectionString);
        await using var db = new OrganizationDbContext(optsBuilder.Options);
        await db.Database.MigrateAsync();
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
}
