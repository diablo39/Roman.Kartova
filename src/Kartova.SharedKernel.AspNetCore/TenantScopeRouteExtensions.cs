using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

public static class TenantScopeRouteExtensions
{
    /// <summary>
    /// Attach this to a MapGroup to require authentication AND tag the route
    /// with <see cref="RequireTenantScopeMarker"/>. <c>TenantScopeMiddleware</c>
    /// wraps the request in an <see cref="Multitenancy.ITenantScope"/>
    /// before endpoint dispatch so parameter binding of tenant-scoped DbContexts
    /// works correctly. See ADR-0090.
    /// </summary>
    public static TBuilder RequireTenantScope<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.WithMetadata(RequireTenantScopeMarker.Instance);
        return builder;
    }
}
