using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.Api.HealthChecks;

/// <summary>
/// ADR-0060 startup probe. Checks (never applies) pending migrations per module —
/// actual migration application is Kartova.Migrator's job exclusively (ADR-0085).
/// Resolves each module's DbContext from <paramref name="migrationsCheckProvider"/>,
/// a dedicated provider built (in Program.cs) via each module's
/// <see cref="IModule.RegisterForMigrator"/> override — the same tenant-scope-free
/// registration Kartova.Migrator itself uses, and the same GetService(module.DbContextType)
/// resolution mechanism, since production's normal registration (AddModuleDbContext,
/// ADR-0090) requires an active per-request ITenantScope that health checks never have.
/// __EFMigrationsHistory is a global, non-tenant table, so none is needed here either.
/// </summary>
public sealed class ModuleMigrationsHealthCheck(
    IModule[] modules,
    ServiceProvider migrationsCheckProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(modules.Select(async module =>
        {
            using var scope = migrationsCheckProvider.CreateScope();
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(module.DbContextType);
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            return pending.Any() ? module.Name : null;
        }));

        var pendingModules = results.Where(name => name is not null).ToList();
        return pendingModules.Count == 0
            ? HealthCheckResult.Healthy("All modules up to date")
            : HealthCheckResult.Unhealthy($"Pending migrations: {string.Join(", ", pendingModules)}");
    }
}
