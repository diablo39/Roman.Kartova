using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// DI extensions for per-request tenant-scoped DbContexts (ADR-0090).
/// </summary>
public static class AddModuleDbContextExtensions
{
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var scope = (TenantScope)sp.GetRequiredService<ITenantScope>();

            // Use the scope's already-open connection so all module DbContexts in this request
            // share the same connection + transaction per ADR-0090.
            options.UseNpgsql(scope.Connection);

            // Fail-fast on SaveChanges if scope is not active.
            options.AddInterceptors(sp.GetRequiredService<TenantScopeRequiredInterceptor>());

            configure?.Invoke(options);
        });

        return services;
    }

    /// <summary>
    /// Registers the scope + required interceptor services. Call once during composition-root wiring.
    /// </summary>
    public static IServiceCollection AddTenantScope(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, TenantContextAccessor>();
        services.AddScoped<ITenantScope, TenantScope>();
        services.AddScoped<TenantScopeRequiredInterceptor>();
        return services;
    }
}
