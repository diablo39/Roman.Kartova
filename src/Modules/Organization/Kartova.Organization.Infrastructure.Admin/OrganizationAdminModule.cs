using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// Endpoint-only module for the Organization admin (BYPASSRLS) routes at
/// <c>/api/v1/admin/organizations</c>. Lives in Infrastructure.Admin so that
/// Infrastructure.Admin → Infrastructure remains the only direction in the
/// reference graph (Infrastructure cannot depend on Infrastructure.Admin).
/// Per ADR-0092 the slug ("organizations") matches the tenant-scoped module's
/// slug — same resource collection, different auth surface.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class OrganizationAdminModule : IModuleEndpoints
{
    // Mirror of OrganizationModule.Slug — both surfaces address the same resource collection,
    // so any future rename must be applied in both places. Kept as a const here (rather than
    // an interface property on IModuleEndpoints) because this class is not an IModule —
    // it exists only to graft admin-route mapping into the IModuleEndpoints loop.
    public const string Slug = "organizations";

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var admin = app.MapAdminModule(Slug);             // /api/v1/admin/organizations
        admin.MapPost("/", AdminOrganizationEndpointDelegates.CreateAsync)
            .WithName("AdminCreateOrganization");
    }
}
