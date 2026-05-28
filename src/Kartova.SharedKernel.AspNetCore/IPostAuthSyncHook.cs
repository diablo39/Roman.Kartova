using System.Security.Claims;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Runs inside <see cref="TenantScopeBeginMiddleware"/> AFTER
/// <see cref="Kartova.SharedKernel.Multitenancy.ITenantScope.BeginAsync"/> succeeds,
/// allowing modules to hook into the post-auth lifecycle (e.g. user-projection sync,
/// invitation acceptance detection). One hook per module — order is not significant.
/// Hooks may resolve and use module DbContexts because the per-request connection +
/// transaction is open when they run, and team memberships have already been populated
/// on <see cref="Kartova.SharedKernel.Multitenancy.ITenantContext"/>.
/// <para>
/// Hooks should be idempotent: the middleware fan-out runs once per matched tenant-scoped
/// endpoint, but hook implementations should not rely on at-most-once semantics across
/// arbitrary request paths.
/// </para>
/// <para>
/// Hooks fire only for endpoints carrying <see cref="RequireTenantScopeMarker"/>; anonymous
/// and admin (non-tenant) routes do not invoke them — by design, because the hook side
/// effects (e.g. <c>users</c> upsert) need an active tenant scope to execute.
/// </para>
/// </summary>
public interface IPostAuthSyncHook
{
    Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct);
}
