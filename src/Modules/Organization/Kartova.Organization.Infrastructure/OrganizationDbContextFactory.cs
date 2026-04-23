using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Enables `dotnet ef migrations add` without a running host.
/// Production connection strings come from IModule.RegisterServices.
/// </summary>
internal sealed class OrganizationDbContextFactory : IDesignTimeDbContextFactory<OrganizationDbContext>
{
    public OrganizationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql("Host=localhost;Database=kartova_design;Username=migrator;Password=dev",
                npg => npg.MigrationsAssembly(typeof(OrganizationDbContextFactory).Assembly.FullName))
            .Options;

        return new OrganizationDbContext(options);
    }
}
