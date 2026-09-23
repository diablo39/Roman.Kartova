using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.Api.HealthChecks;

/// <summary>
/// ADR-0060 startup probe. Checks (never applies) pending migrations per module —
/// actual migration application is Kartova.Migrator's job exclusively (ADR-0085).
/// Constructs a plain, non-tenant-scoped DbContext per module via
/// <paramref name="dbContextFactories"/> rather than resolving module.DbContextType
/// from the app's DI container: production registers module DbContexts via
/// AddModuleDbContext (ADR-0090), which sources its connection from the per-request
/// ITenantScope — unavailable outside a request with an active tenant scope, and
/// irrelevant anyway since __EFMigrationsHistory is a global, non-tenant table.
/// </summary>
public sealed class ModuleMigrationsHealthCheck(
    IModule[] modules,
    IReadOnlyDictionary<Type, Func<DbContext>> dbContextFactories) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pendingModules = new List<string>();
        foreach (var module in modules)
        {
            await using var db = dbContextFactories[module.DbContextType]();
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Any())
            {
                pendingModules.Add(module.Name);
            }
        }

        return pendingModules.Count == 0
            ? HealthCheckResult.Healthy("All modules up to date")
            : HealthCheckResult.Unhealthy($"Pending migrations: {string.Join(", ", pendingModules)}");
    }
}
