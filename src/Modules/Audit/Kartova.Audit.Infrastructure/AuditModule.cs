using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Postgres;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Audit bounded-context module (ADR-0082 / ADR-0018). Registers the tenant-scoped
/// <see cref="AuditDbContext"/> via <c>AddModuleDbContext</c> so the writer's insert rides the
/// per-request <c>ITenantScope</c> connection + transaction (ADR-0090), and binds the
/// SharedKernel <see cref="IAuditWriter"/> port to <see cref="AuditWriter"/>.
/// <para>
/// Phase 1 has no HTTP surface. <see cref="IModuleEndpoints.MapEndpoints"/> is intentionally
/// empty — routes arrive in Phase 2 when consumers need a query endpoint.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AuditModule : IModule, IModuleEndpoints
{
    public string Name => "audit";

    public string Slug => "audit";

    public Type DbContextType => typeof(AuditDbContext);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AuditDbContext>(npg =>
            npg.MigrationsAssembly(typeof(AuditDbContext).Assembly.FullName));

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditChainVerifier>();
    }

    public void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
    {
        var cs = KartovaConnectionStrings.RequireMain(configuration);
        services.AddDbContext<AuditDbContext>(opts =>
            opts.UseNpgsql(cs, npg => npg.MigrationsAssembly(
                typeof(AuditDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(AuditModule).Assembly);
    }

    /// <summary>
    /// Phase 1: no HTTP surface. Routes arrive in Phase 2.
    /// </summary>
    public void MapEndpoints(IEndpointRouteBuilder app) { }
}
