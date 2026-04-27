using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartova.Testing.Auth;

/// <summary>
/// Shared setup for integration-test fixtures that bring up a fresh Postgres container
/// and need the same role/grants surface as production (ADR-0085, ADR-0090).
/// </summary>
[ExcludeFromCodeCoverage]
public static class PostgresTestBootstrap
{
    public const string MigratorRole = "migrator";
    public const string AppRole = "kartova_app";
    public const string BypassRole = "kartova_bypass_rls";

    private const string AppPassword = "dev";
    private const string MigratorPassword = "dev";
    private const string BypassPassword = "dev_only";

    // Mirrors docker/postgres/init.sql exactly so the test fixture's permission surface
    // matches production. kartova_app gets USAGE on schema only (no CREATE); only the
    // BYPASSRLS role retains CREATE for the admin-bypass path.
    private static readonly string SeedRolesSql = $"""
        CREATE ROLE {MigratorRole} WITH LOGIN PASSWORD '{MigratorPassword}' CREATEDB;
        CREATE ROLE {AppRole} WITH LOGIN PASSWORD '{AppPassword}';
        CREATE ROLE {BypassRole} WITH LOGIN PASSWORD '{BypassPassword}' BYPASSRLS;
        GRANT CONNECT ON DATABASE kartova TO {AppRole}, {BypassRole};
        ALTER SCHEMA public OWNER TO {MigratorRole};
        GRANT USAGE ON SCHEMA public TO {AppRole};
        GRANT USAGE, CREATE ON SCHEMA public TO {BypassRole};
        ALTER DEFAULT PRIVILEGES FOR ROLE {MigratorRole} IN SCHEMA public
            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppRole}, {BypassRole};
        ALTER DEFAULT PRIVILEGES FOR ROLE {MigratorRole} IN SCHEMA public
            GRANT USAGE, SELECT ON SEQUENCES TO {AppRole}, {BypassRole};
        """;

    public static async Task SeedRolesAndSchemaAsync(string adminConnectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SeedRolesSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static string ConnectionStringFor(string baseConnectionString, string role) =>
        new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Username = role,
            Password = PasswordFor(role),
        }.ToString();

    public static async Task RunMigrationsAsync<TContext>(
        string migratorConnectionString,
        Func<DbContextOptions<TContext>, TContext> factory,
        CancellationToken ct = default)
        where TContext : DbContext
    {
        var opts = new DbContextOptionsBuilder<TContext>().UseNpgsql(migratorConnectionString).Options;
        await using var db = factory(opts);
        await db.Database.MigrateAsync(ct);
    }

    private static string PasswordFor(string role) => role switch
    {
        MigratorRole => MigratorPassword,
        AppRole => AppPassword,
        BypassRole => BypassPassword,
        _ => throw new ArgumentException($"Unknown role '{role}'", nameof(role)),
    };
}
