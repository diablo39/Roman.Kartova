using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Per ADR-0092: every module's HTTP routes are declared via these helpers,
/// which apply the URL convention and the auth/tenant-scope shape mechanically.
/// </summary>
public static class ModuleRouteExtensions
{
    /// <summary>
    /// Tenant-scoped module routes at <c>/api/v1/{slug}</c>. The slug IS the URL segment;
    /// modules with a plural primary collection (e.g. Organization → "organizations")
    /// declare it as such so the URL reads naturally without a doubled segment.
    /// See ADR-0092.
    /// </summary>
    public static RouteGroupBuilder MapTenantScopedModule(this IEndpointRouteBuilder app, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return app.MapGroup($"/api/v1/{slug}").RequireTenantScope();
    }

    /// <summary>
    /// Admin (platform-admin) module routes at <c>/api/v1/admin/{slug}</c>.
    /// The whole admin URL space is gated by the platform-admin role.
    /// See ADR-0092.
    /// </summary>
    public static RouteGroupBuilder MapAdminModule(this IEndpointRouteBuilder app, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return app.MapGroup($"/api/v1/admin/{slug}")
            .RequireAuthorization(p => p.RequireRole(KartovaRoles.PlatformAdmin));
    }
}
