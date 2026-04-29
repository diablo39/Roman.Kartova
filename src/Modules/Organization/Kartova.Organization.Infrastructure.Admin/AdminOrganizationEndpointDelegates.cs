using System.Diagnostics.CodeAnalysis;
using Kartova.Organization.Application;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace Kartova.Organization.Infrastructure.Admin;

internal static class AdminOrganizationEndpointDelegates
{
    [ExcludeFromCodeCoverage]
    public sealed record CreateOrganizationRequest(string Name);

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
