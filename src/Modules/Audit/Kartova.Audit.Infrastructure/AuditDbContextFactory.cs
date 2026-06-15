using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Enables `dotnet ef migrations add` without a running host.
/// Production connection strings come from IModule.RegisterServices.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql("Host=localhost;Database=kartova_design;Username=migrator;Password=dev",
                npg => npg.MigrationsAssembly(typeof(AuditDbContextFactory).Assembly.FullName))
            .Options;
        return new AuditDbContext(options);
    }
}
