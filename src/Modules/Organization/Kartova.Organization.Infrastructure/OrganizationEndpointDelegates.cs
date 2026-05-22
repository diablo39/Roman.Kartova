using System.Security.Claims;
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace Kartova.Organization.Infrastructure;

internal static class OrganizationEndpointDelegates
{
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
        return Results.Ok(new AdminOnlyResponse("ok"));
    }

    internal static IResult GetMePermissions(ClaimsPrincipal user)
    {
        // Spec §3 Decision #2: each user holds exactly one realm role.
        // FirstOrDefault is the explicit choice — if multiple ClaimTypes.Role
        // claims somehow arrive on the principal, only the first is surfaced.
        var role = user.FindAll(ClaimTypes.Role)
                       .Select(c => c.Value)
                       .FirstOrDefault() ?? string.Empty;

        var permissions = user.FindAll(KartovaClaims.Permission)
                              .Select(c => c.Value)
                              .ToArray();

        return Results.Ok(new MePermissionsResponse(role, permissions));
    }
}
