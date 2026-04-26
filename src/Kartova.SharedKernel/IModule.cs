using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.SharedKernel;

/// <summary>
/// Every bounded-context module implements this interface. The composition
/// root enumerates all modules and invokes them in order.
/// Enforced by NetArchTest boundary rules (ADR-0082).
/// </summary>
public interface IModule
{
    string Name { get; }
    Type DbContextType { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Migrator-specific DI registration. Default delegates to <see cref="RegisterServices"/>
    /// — override when the runtime registration uses tenant-scoped plumbing that the
    /// migrator cannot satisfy (e.g. <c>AddModuleDbContext</c> requires <c>ITenantScope</c>).
    /// Migrations run DDL, not DML, so RLS/tenant-scope does not apply.
    /// </summary>
    void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
        => RegisterServices(services, configuration);

    void ConfigureWolverine(WolverineOptions options);
}
