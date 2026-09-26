using System.Security.Claims;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// ADR-0118: operator identity from the <c>kartova-platform</c> JWT. Claims only — no DB,
/// no tenant scope (an operator has no tenant). Claim names are raw OIDC because the
/// PlatformAdmin scheme sets <c>MapInboundClaims = false</c>.
/// </summary>
internal static class AdminSessionEndpointDelegates
{
    internal static IResult GetMe(ClaimsPrincipal user)
    {
        if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var userId))
        {
            return Results.Problem(
                type: ProblemTypes.InvalidToken,
                title: "Invalid token",
                detail: "The operator token has no usable 'sub' claim.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var email = user.FindFirst("email")?.Value ?? string.Empty;
        var name = user.FindFirst("name")?.Value;
        return Results.Ok(new AdminMeResponse(userId, email, string.IsNullOrWhiteSpace(name) ? email : name));
    }
}
