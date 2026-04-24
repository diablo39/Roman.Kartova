using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
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

// Kartova.Migrator is the sole DDL owner (ADR-0085). Slice 2 has no Wolverine
// persistence — see docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md.
// When a later slice enables Wolverine persistence (outbox), the `wolverine.*`
// schema must be created here under the `migrator` role (Option A — host Wolverine
// in this process and call JasperFx IStatefulResource / IMessageStore.Admin.MigrateAsync).

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

logger.LogInformation("All migrations applied. Exiting.");
return 0;
