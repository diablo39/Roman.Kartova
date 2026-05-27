using System.Security.Claims;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Runs inside <see cref="TenantClaimsTransformation"/> after tenant + role claim flattening,
/// allowing modules to hook into the post-auth lifecycle (e.g. user-projection sync,
/// invitation acceptance detection). One hook per module — order is not significant.
/// Hooks should be idempotent: <see cref="TenantClaimsTransformation"/> may be invoked
/// multiple times per request.
/// </summary>
public interface IPostAuthSyncHook
{
    Task ExecuteAsync(ClaimsPrincipal principal, CancellationToken ct);
}
