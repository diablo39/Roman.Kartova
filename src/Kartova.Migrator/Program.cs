using Kartova.Catalog.Infrastructure;
using Kartova.Migrator;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

if (args.Contains("--seed=dev"))
{
    // Defense-in-depth: a misconfigured pipeline must not write fixture rows into a
    // customer database. The compose/helm convention is "only dev profile passes the flag";
    // this guard makes the contract enforceable.
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "--seed=dev refused: dev fixtures must not run in Production.");
    }
    await DevSeed.RunAsync(host.Services.GetRequiredService<IConfiguration>(), logger);
}

logger.LogInformation("All migrations applied. Exiting.");
return 0;
