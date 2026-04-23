using Kartova.Organization.Application;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Organization bounded-context module (ADR-0082).
///
/// Registers the tenant-scoped <see cref="OrganizationDbContext"/> via
/// <c>AddModuleDbContext</c> so it participates in the per-request
/// <c>ITenantScope</c> (shared connection + transaction, <c>SET LOCAL app.current_tenant_id</c>)
/// per ADR-0090.
///
/// The admin bypass path (<see cref="Admin.AdminOrganizationDbContext"/>) is registered
/// separately by <c>Kartova.Organization.Infrastructure.Admin</c>'s composition extension
/// (<c>AddOrganizationAdmin</c>) because <c>Infrastructure</c> cannot reference
/// <c>Infrastructure.Admin</c> without creating a circular project reference.
/// </summary>
public sealed class OrganizationModule : IModule
{
    public string Name => "organization";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Tenant-scoped DbContext — connection flows from ITenantScope per ADR-0090.
        // Migrations assembly pinned so `dotnet ef` and runtime agree.
        services.AddModuleDbContext<OrganizationDbContext>(opts =>
            opts.UseNpgsql(npg => npg.MigrationsAssembly(
                typeof(OrganizationDbContext).Assembly.FullName)));

        services.AddScoped<IOrganizationQueries, OrganizationQueries>();
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(OrganizationModule).Assembly);
        // Handlers and publish routes arrive in later slices.
    }
}
