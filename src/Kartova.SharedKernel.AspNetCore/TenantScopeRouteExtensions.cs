using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

public static class TenantScopeRouteExtensions
{
    /// <summary>
    /// Marks a route group as tenant-scoped. Wires three things:
    /// <list type="bullet">
    ///   <item><see cref="AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization{TBuilder}(TBuilder, string[])"/>
    ///         — tenant-scoped routes are authenticated by definition (need a JWT to
    ///         extract <c>tenant_id</c>).</item>
    ///   <item><see cref="RequireTenantScopeMarker"/> metadata — <see cref="TenantScopeBeginMiddleware"/>
    ///         uses it to identify endpoints that should open a tenant scope before
    ///         parameter binding.</item>
    ///   <item><see cref="TenantScopeCommitEndpointFilter"/> — commits the scope's
    ///         transaction between handler return and <see cref="IResult.ExecuteAsync"/>,
    ///         preserving the durability promise from ADR-0090.</item>
    /// </list>
    /// See ADR-0090 §Addendum (2026-04-28) for why this is a two-piece adapter.
    /// </summary>
    public static RouteGroupBuilder RequireTenantScope(this RouteGroupBuilder builder)
    {
        builder.RequireAuthorization();
        builder.WithMetadata(RequireTenantScopeMarker.Instance);
        builder.AddEndpointFilter<TenantScopeCommitEndpointFilter>();
        return builder;
    }
}
