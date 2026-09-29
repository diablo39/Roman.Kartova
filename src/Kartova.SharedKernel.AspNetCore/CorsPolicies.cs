namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// CORS policy names. <see cref="TenantWeb"/> is the middleware default for every route;
/// <see cref="AdminWeb"/> is bound per endpoint to <c>/api/v1/admin/*</c> by
/// <see cref="ModuleRouteExtensions.MapAdminModule"/> and admits only the web-admin origin (ADR-0118).
/// </summary>
public static class CorsPolicies
{
    public const string TenantWeb = "KartovaWeb";
    public const string AdminWeb = "KartovaAdminWeb";
}
