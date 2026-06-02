using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// DI extensions for Postgres-advisory-lock-backed <see cref="IDistributedLock"/> (ADR-0099).
/// </summary>
public static class AddPostgresDistributedLocksExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresAdvisoryLock"/> as the cluster-wide <see cref="IDistributedLock"/>
    /// implementation. Requires an <c>NpgsqlDataSource</c> already registered (e.g. via
    /// <c>AddNpgsqlDataSource</c>).
    /// </summary>
    public static IServiceCollection AddPostgresDistributedLocks(this IServiceCollection services)
    {
        services.AddSingleton<IDistributedLock, PostgresAdvisoryLock>();
        return services;
    }
}
