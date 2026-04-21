using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Enables `dotnet ef migrations add` without a running host.
/// Production connection strings come from IModule.RegisterServices.
/// </summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=kartova_design;Username=migrator;Password=dev",
                npg => npg.MigrationsAssembly(typeof(CatalogDbContextFactory).Assembly.FullName))
            .Options;

        return new CatalogDbContext(options);
    }
}
