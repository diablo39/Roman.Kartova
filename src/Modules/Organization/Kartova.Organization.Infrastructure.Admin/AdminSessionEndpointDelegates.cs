using System.Security.Claims;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// ADR-0118: operator identity from the <c>kartova-platform</c> JWT. Claims only — no DB,
/// no tenant scope (an operator has no tenant). Claim names are raw OIDC because the
/// PlatformAdmin scheme sets <c>MapInboundClaims = false</c>.
/// </summary>
internal static class AdminSessionEndpointDelegates
{
    private const string LoggerCategory = "Kartova.Organization.Admin.AdminSession";

    internal static IResult GetMe(ClaimsPrincipal user, ILoggerFactory loggerFactory)
    {
        var sub = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId))
        {
            // This 401 is ProblemDetails-only — no `WWW-Authenticate` challenge is issued, because
            // the request already passed scheme authentication (a valid PlatformAdmin-scheme token);
            // this is a claim-shape failure, not a scheme rejection. It is reachable only by an
            // authenticated operator token that lacks a GUID `sub`, which real KeyCloak never issues.
            // Isolation tests tell this branch apart from a scheme rejection by the absence of the
            // `invalid_token` value in `WWW-Authenticate` (scheme rejections carry it; this does not).
            // Never log the raw claim value — only whether it was missing or present-but-unparsable.
            loggerFactory.CreateLogger(LoggerCategory).LogWarning(
                "Operator token without a usable 'sub' claim reached /api/v1/admin/session/me. Sub claim was {SubState}.",
                sub is null ? "missing" : "unparsable");

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
