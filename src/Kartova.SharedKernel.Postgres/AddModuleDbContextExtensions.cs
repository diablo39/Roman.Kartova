using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// DI extensions for per-request tenant-scoped DbContexts (ADR-0090).
/// </summary>
public static class AddModuleDbContextExtensions
{
    /// <summary>
    /// Registers a tenant-scoped DbContext that shares the per-request <see cref="INpgsqlTenantScope"/>
    /// connection + transaction (ADR-0090). Provider-specific configuration (e.g. MigrationsAssembly)
    /// is applied inside the same <c>UseNpgsql</c> call as the connection so callers cannot
    /// accidentally invoke <c>UseNpgsql</c> twice and reset the scope's connection.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptions = null)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            // Use the scope's already-open connection so all module DbContexts in this request
            // share the same connection + transaction per ADR-0090. Provider-specific options
            // are applied inside the same call to avoid a second UseNpgsql resetting the connection.
            var scope = sp.GetRequiredService<INpgsqlTenantScope>();
            options.UseNpgsql(scope.Connection, npg => npgsqlOptions?.Invoke(npg));

            // Fail-fast on SaveChanges if scope is not active.
            options.AddInterceptors(sp.GetRequiredService<TenantScopeRequiredInterceptor>());
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
