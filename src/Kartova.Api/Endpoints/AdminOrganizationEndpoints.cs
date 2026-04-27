using System.Diagnostics.CodeAnalysis;
using Kartova.Organization.Application;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Api.Endpoints;

internal static class AdminOrganizationEndpoints
{
    [ExcludeFromCodeCoverage]
    public sealed record CreateOrganizationRequest(string Name);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/organizations", CreateAsync);
    }

    private const int NameMaxLength = 100;

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
        if (request.Name.Length > NameMaxLength)
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid name",
                detail: $"Name must be {NameMaxLength} characters or fewer.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var org = await commands.CreateAsync(request.Name, ct);
        // No Location header until a GET-by-id endpoint exists for this resource.
        return Results.Json(org, statusCode: StatusCodes.Status201Created);
    }
}
