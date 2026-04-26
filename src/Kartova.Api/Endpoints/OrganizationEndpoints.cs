using Kartova.Organization.Application;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Api.Endpoints;

internal static class OrganizationEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/organizations/me", GetMeAsync);

        // Admin role demo endpoint — proves role-based authorization works end-to-end.
        group.MapGet("/organizations/me/admin-only", GetAdminOnlyAsync)
            .RequireAuthorization(policy => policy.RequireRole(KartovaRoles.OrgAdmin));
    }

    internal static async Task<IResult> GetMeAsync(IOrganizationQueries queries, CancellationToken ct)
    {
        var org = await queries.GetCurrentAsync(ct);
        if (org is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization not found",
                detail: "The current tenant has no visible Organization row.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(org);
    }

    internal static IResult GetAdminOnlyAsync()
    {
        return Results.Ok(new { message = "ok" });
    }
}
