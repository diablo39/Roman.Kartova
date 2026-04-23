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

// The migrator doesn't route Kafka messages, but Wolverine may want its own tables
// (outbox persistence) — we still register schema so migrations include them in Slice 3.
// For Slice 1 we skip Wolverine bootstrap in the migrator itself; wolverine tables are
// created lazily by the API.

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
