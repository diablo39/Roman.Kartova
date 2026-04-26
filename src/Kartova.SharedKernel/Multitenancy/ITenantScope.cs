namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Owns one physical connection + one transaction per request, with
/// <c>app.current_tenant_id</c> set via <c>SET LOCAL</c>.
/// Only transport adapters (HTTP endpoint filter, Wolverine/Kafka middleware)
/// call <see cref="BeginAsync"/>. Handlers never touch this directly;
/// they just use DbContexts registered via <c>AddModuleDbContext{T}</c>.
/// See ADR-0090.
/// </summary>
public interface ITenantScope
{
    Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct);
    bool IsActive { get; }
}
