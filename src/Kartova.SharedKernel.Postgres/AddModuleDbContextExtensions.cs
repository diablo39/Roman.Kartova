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
            // Use the scope's already-open connection so all module DbContexts in this request
            // share the same connection + transaction per ADR-0090.
            var scope = sp.GetRequiredService<INpgsqlTenantScope>();
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
        services.AddScoped<TenantScope>();
        services.AddScoped<ITenantScope>(sp => sp.GetRequiredService<TenantScope>());
        services.AddScoped<INpgsqlTenantScope>(sp => sp.GetRequiredService<TenantScope>());
        services.AddScoped<TenantScopeRequiredInterceptor>();
        return services;
    }
}
