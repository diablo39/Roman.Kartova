namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Endpoint metadata marker applied by <see cref="TenantScopeRouteExtensions.RequireTenantScope"/>.
/// <see cref="TenantScopeBeginMiddleware"/> inspects matched endpoints for this marker to
/// decide whether to open an <c>ITenantScope</c> for the request. The commit-filter is
/// attached directly via <c>AddEndpointFilter</c> on the same route group, so the marker
/// is only consumed by the begin-middleware. See ADR-0090 §Addendum (2026-04-28).
/// </summary>
public sealed class RequireTenantScopeMarker
{
    public static readonly RequireTenantScopeMarker Instance = new();
}
