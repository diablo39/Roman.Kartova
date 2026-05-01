using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

IModule[] modules =
[
    new CatalogModule(),
    new OrganizationModule(),
];

foreach (var module in modules)
{
    module.RegisterForMigrator(builder.Services, builder.Configuration);
}

// Sole DDL owner (ADR-0085). Wolverine persistence schema is added here when re-enabled.

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Kartova migrator starting; {ModuleCount} module(s) registered.", modules.Length);

foreach (var module in modules)
{
    using var scope = host.Services.CreateScope();
    logger.LogInformation("Applying migrations for module '{Module}'...", module.Name);

    // Each module declares its primary DbContext via IModule.DbContextType.
    var dbContext = (DbContext?)scope.ServiceProvider.GetService(module.DbContextType)
        ?? throw new InvalidOperationException(
            $"DbContext for module '{module.Name}' not registered.");

    await dbContext.Database.MigrateAsync();
    logger.LogInformation("Module '{Module}' migrated.", module.Name);
}

// E-01.F-01.S-04: dev-only seed for Org A so a developer who runs `docker compose up`
// has a working tenant for the KeyCloak admin@orga / member@orga users (whose
// tenant_id claim is 11111111-1111-1111-1111-111111111111). Idempotent (ON CONFLICT
// DO NOTHING). Toggled via --seed=dev so production migrator runs never seed.
if (args.Contains("--seed=dev"))
{
    await SeedDevAsync(host.Services.GetRequiredService<IConfiguration>(), logger);
}

logger.LogInformation("All migrations applied. Exiting.");
return 0;

static async Task SeedDevAsync(IConfiguration config, ILogger logger)
{
    var connection = KartovaConnectionStrings.RequireMain(config);
    await using var conn = new NpgsqlConnection(connection);
    await conn.OpenAsync();

    // Toggle FORCE RLS off for the seed insert. Same pattern as the slice-4 displayName
    // backfill migration: the migrator role owns `organizations` but lacks BYPASSRLS,
    // so FORCE makes the policy apply to the owner too. NO FORCE → seed → FORCE.
    await using (var off = conn.CreateCommand())
    {
        off.CommandText = "ALTER TABLE organizations NO FORCE ROW LEVEL SECURITY;";
        await off.ExecuteNonQueryAsync();
    }
    try
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO organizations (id, tenant_id, name, created_at)
            VALUES ($1, $2, $3, now())
            ON CONFLICT (id) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        cmd.Parameters.AddWithValue("Org A");
        var rows = await cmd.ExecuteNonQueryAsync();
        logger.LogInformation("Dev seed: Org A {Result}.", rows == 1 ? "inserted" : "already present");
    }
    finally
    {
        await using var on = conn.CreateCommand();
        on.CommandText = "ALTER TABLE organizations FORCE ROW LEVEL SECURITY;";
        await on.ExecuteNonQueryAsync();
    }
}
