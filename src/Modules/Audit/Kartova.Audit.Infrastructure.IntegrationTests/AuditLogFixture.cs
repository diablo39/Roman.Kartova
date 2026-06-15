using System.Diagnostics.CodeAnalysis;
using Kartova.Audit.Infrastructure;
using Kartova.Testing.Auth;
using Testcontainers.PostgreSql;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// Spins up a Postgres 18 container with production-matching roles
/// (migrator / kartova_app / kartova_bypass_rls), migrates the audit schema, and
/// exposes per-role connection strings.
///
/// Lifecycle: call <see cref="CreateAsync"/> once per test assembly
/// (from an <c>[AssemblyInitialize]</c> handler) and dispose in
/// <c>[AssemblyCleanup]</c>. The container is shared across all test classes to
/// keep total wall-clock cost low — see <see cref="IntegrationTestAssemblySetup"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AuditLogFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _pg;

    public string MigratorConnectionString { get; }
    public string AppConnectionString { get; }
    public string BypassConnectionString { get; }

    private AuditLogFixture(PostgreSqlContainer pg)
    {
        _pg = pg;
        var baseCs = pg.GetConnectionString();
        MigratorConnectionString = PostgresTestBootstrap.ConnectionStringFor(baseCs, PostgresTestBootstrap.MigratorRole);
        AppConnectionString = PostgresTestBootstrap.ConnectionStringFor(baseCs, PostgresTestBootstrap.AppRole);
        BypassConnectionString = PostgresTestBootstrap.ConnectionStringFor(baseCs, PostgresTestBootstrap.BypassRole);
    }

    /// <summary>
    /// Starts the Postgres container, seeds production-equivalent roles, and migrates
    /// <see cref="AuditDbContext"/> (including the RLS + REVOKE SQL from the migration).
    /// </summary>
    public static async Task<AuditLogFixture> CreateAsync(CancellationToken ct = default)
    {
        var pg = new PostgreSqlBuilder()
            .WithImage("postgres:18-alpine")
            .WithDatabase("kartova")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await pg.StartAsync(ct);

        var adminCs = pg.GetConnectionString();
        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(adminCs, ct);

        var migratorCs = PostgresTestBootstrap.ConnectionStringFor(adminCs, PostgresTestBootstrap.MigratorRole);
        await PostgresTestBootstrap.RunMigrationsAsync<AuditDbContext>(
            migratorCs,
            opts => new AuditDbContext(opts),
            ct);

        return new AuditLogFixture(pg);
    }

    public async ValueTask DisposeAsync() => await _pg.DisposeAsync();
}
