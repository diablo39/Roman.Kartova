using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kartova.Api.HealthChecks;

/// <summary>
/// ADR-0060 startup probe. Mirrors Kartova.Migrator/Program.cs's
/// module.DbContextType resolution but only checks (never applies) pending
/// migrations — actual migration application is Kartova.Migrator's job
/// exclusively (ADR-0085).
/// </summary>
public sealed class ModuleMigrationsHealthCheck(
    IServiceScopeFactory scopeFactory,
    IModule[] modules) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pendingModules = new List<string>();
        foreach (var module in modules)
        {
            using var scope = scopeFactory.CreateScope();
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(module.DbContextType);
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
