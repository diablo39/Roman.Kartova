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

    internal static IResult GetMePermissionsAsync(ClaimsPrincipal user)
    {
        var role = user.FindAll(ClaimTypes.Role)
                       .Select(c => c.Value)
                       .FirstOrDefault() ?? string.Empty;

        var permissions = user.FindAll(KartovaClaims.Permission)
                              .Select(c => c.Value)
                              .ToArray();

        return Results.Ok(new MePermissionsResponse(role, permissions));
    }
}
