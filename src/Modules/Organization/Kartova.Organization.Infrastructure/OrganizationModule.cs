using System.Diagnostics.CodeAnalysis;
using Kartova.Organization.Application;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
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
[ExcludeFromCodeCoverage]
public sealed class OrganizationModule : IModule, IModuleEndpoints
{
    public string Name => "organization";

    public string Slug => "organizations";

    public Type DbContextType => typeof(OrganizationDbContext);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var tenant = app.MapTenantScopedModule(Slug);     // /api/v1/organizations
        tenant.MapGet("/me", OrganizationEndpointDelegates.GetMeAsync);
        tenant.MapGet("/me/admin-only", OrganizationEndpointDelegates.GetAdminOnlyAsync)
            .RequireAuthorization(p => p.RequireRole(KartovaRoles.OrgAdmin));
    }

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Tenant-scoped DbContext — connection flows from ITenantScope per ADR-0090.
        // Migrations assembly pinned so `dotnet ef` and runtime agree.
        services.AddModuleDbContext<OrganizationDbContext>(npg =>
            npg.MigrationsAssembly(typeof(OrganizationDbContext).Assembly.FullName));

        services.AddScoped<IOrganizationQueries, OrganizationQueries>();
    }

    /// <summary>
    /// Migrator-specific registration: plain <c>AddDbContext</c> (no tenant scope).
    /// Migrations are DDL and run under the migrator role with RLS-bypass; they do
    /// not need the per-request shared connection/transaction from <c>ITenantScope</c>
    /// that <c>AddModuleDbContext</c> otherwise requires.
    /// </summary>
    public void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString(KartovaConnectionStrings.Main)
            ?? throw new InvalidOperationException(
                $"Connection string '{KartovaConnectionStrings.Main}' is required. Set it via ConnectionStrings__{KartovaConnectionStrings.Main} env var.");

        services.AddDbContext<OrganizationDbContext>(opts =>
            opts.UseNpgsql(cs, npg => npg.MigrationsAssembly(
                typeof(OrganizationDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(OrganizationModule).Assembly);
        // Handlers and publish routes arrive in later slices.
    }
}
