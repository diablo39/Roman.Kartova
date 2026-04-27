using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.Catalog.Infrastructure;

[ExcludeFromCodeCoverage]
public sealed class CatalogModule : IModule
{
    public string Name => "catalog";

    public Type DbContextType => typeof(CatalogDbContext);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Tenant-scoped DbContext per ADR-0090. Connection flows from ITenantScope —
        // raw AddDbContext would silently bypass RLS for any future Catalog entity.
        services.AddModuleDbContext<CatalogDbContext>(npg =>
            npg.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName));
    }

    public void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString(KartovaConnectionStrings.Main)
            ?? throw new InvalidOperationException(
                $"Connection string '{KartovaConnectionStrings.Main}' is required. Set it via ConnectionStrings__{KartovaConnectionStrings.Main} env var.");

        services.AddDbContext<CatalogDbContext>(opts =>
            opts.UseNpgsql(cs, npg => npg.MigrationsAssembly(
                typeof(CatalogDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(CatalogModule).Assembly);
        // Handlers and publish routes arrive in Slice 3.
    }
}
