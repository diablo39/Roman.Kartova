using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

public static class TenantScopeRouteExtensions
{
    /// <summary>
    /// Attach this to a MapGroup to require authentication AND a tenant_id claim,
    /// and wrap every endpoint in an ITenantScope. See ADR-0090.
    /// </summary>
    public static TBuilder RequireTenantScope<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        EndpointFilterExtensions.AddEndpointFilter<TBuilder, TenantScopeEndpointFilter>(builder);
        return builder;
    }
}
