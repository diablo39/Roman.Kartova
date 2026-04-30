using System.Diagnostics.CodeAnalysis;
using Kartova.Organization.Infrastructure;
using Kartova.Testing.Auth;
using Npgsql;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Organization-specific fixture. All cross-module plumbing (Postgres container,
/// role bootstrap, JWT signer wiring, env-var wiring of the Kartova.Api host,
/// JWT minting helpers) lives in <see cref="KartovaApiFixtureBase"/>; this type
/// declares the DbContext to migrate plus the BYPASSRLS-seeding helper that
/// Organization integration tests use to plant rows for cross-tenant probes.
/// </summary>
[ExcludeFromCodeCoverage]
public class KartovaApiFixture : KartovaApiFixtureBase
{
    protected override Task RunModuleMigrationsAsync(string migratorConnectionString) =>
        PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            migratorConnectionString,
            opts => new OrganizationDbContext(opts));

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
