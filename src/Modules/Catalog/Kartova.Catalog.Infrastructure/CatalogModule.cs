using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel;
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
        var connectionString = configuration.GetConnectionString("Kartova")
            ?? throw new InvalidOperationException(
                "Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.");

        services.AddDbContext<CatalogDbContext>(opts =>
            opts.UseNpgsql(connectionString, npg =>
                npg.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(CatalogModule).Assembly);
        // Handlers and publish routes arrive in Slice 3.
    }
}
