using Kartova.Organization.Application;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Api.Endpoints;

internal static class AdminOrganizationEndpoints
{
    public sealed record CreateOrganizationRequest(string Name);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/organizations", CreateAsync);
    }

    internal static async Task<IResult> CreateAsync(
        CreateOrganizationRequest request,
        IAdminOrganizationCommands commands,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid name",
                detail: "Name must not be empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var org = await commands.CreateAsync(request.Name, ct);
        return Results.Created($"/api/v1/organizations/{org.Id}", org);
    }
}
