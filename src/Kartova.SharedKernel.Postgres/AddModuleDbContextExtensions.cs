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

            // Enlist DbContext transaction tracking in the shared scope transaction
            // so EF Core writes participate in the per-request atomic unit (ADR-0090).
            // Defensive fallback for any path that bypasses the eager enlistment below
            // (e.g. external code that builds a DbContext outside this DI flow).
            options.AddInterceptors(sp.GetRequiredService<EnlistInTenantScopeInterceptor>());
        });

        // Replace the AddDbContext registration of TContext with a factory that eagerly
        // enlists the new DbContext in the scope's transaction. EF Core's interceptors
        // don't fire when the underlying connection is already open (which is the ADR-0090
        // norm — the scope opens it), so without eager enlistment Database.CurrentTransaction
        // would only become non-null after the first command. Eager enlistment makes EF's
        // public API observably consistent immediately after the DbContext is resolved.
        // Match exactly the descriptor shape produced by AddDbContext<TContext>: a Scoped
        // type registration where ImplementationType == TContext. AddDbContextPool registers
        // TContext via a factory (ImplementationFactory != null, ImplementationType == null),
        // so a future switch to pooling causes Single() to throw at startup with a clear error
        // rather than silently stripping pooling here.
        var existing = services.Single(d =>
            d.ServiceType == typeof(TContext)
            && d.Lifetime == ServiceLifetime.Scoped
            && d.ImplementationType == typeof(TContext));
        services.Remove(existing);
        services.Add(new ServiceDescriptor(
            typeof(TContext),
            sp =>
            {
                var options = sp.GetRequiredService<DbContextOptions<TContext>>();
                var ctx = ActivatorUtilities.CreateInstance<TContext>(sp, options);
                var scope = sp.GetRequiredService<INpgsqlTenantScope>();
                if (scope.IsActive && ctx.Database.CurrentTransaction is null)
                {
                    ctx.Database.UseTransaction(scope.Transaction);
                }
                return ctx;
            },
            existing.Lifetime));

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
        services.AddScoped<EnlistInTenantScopeInterceptor>();
        return services;
    }
}
