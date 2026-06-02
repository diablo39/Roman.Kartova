using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Organization.Infrastructure.Admin;

internal static class InvitationAcceptRoutes
{
    internal const string RateLimitPolicy = "invitation-accept";

    public static void MapTo(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/invitations")
            .RequireRateLimiting(RateLimitPolicy);

        group.MapGet("/accept", GetContextAsync)
            .WithName("GetInvitationAcceptContext")
            .Produces<InvitationAcceptContext>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);

        group.MapPost("/accept", AcceptAsync)
            .WithName("AcceptInvitation")
            .Produces<AcceptInvitationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> GetContextAsync(
        string token,
        [FromServices] AcceptInvitationHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        NoReferrer(ctx);
        var result = await handler.GetContextAsync(token, ct);
        return result switch
        {
            GetAcceptContextResult.Ok ok => Results.Ok(ok.Context),
            GetAcceptContextResult.Failed { Error: AcceptInvitationError.NotFound } => Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Invitation not found",
                detail: "No invitation matches the supplied token.",
                statusCode: StatusCodes.Status404NotFound),
            // Deliberate: GET collapses expired/revoked/already-used into one generic 410 (no enumeration) — see spec §5.1 + deep-review nit 3.
            GetAcceptContextResult.Failed => Results.Problem(
                type: ProblemTypes.InvitationGone,
                title: "Invitation no longer valid",
                detail: "The invitation has expired, been revoked, or was already accepted.",
                statusCode: StatusCodes.Status410Gone),
            _ => Results.Problem(
                type: ProblemTypes.InternalServerError,
                title: "Unexpected error",
                statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> AcceptAsync(
        AcceptInvitationRequest body,
        [FromServices] AcceptInvitationHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        NoReferrer(ctx);
        var result = await handler.AcceptAsync(body.Token, body.Password, body.DisplayName, ct);
        return result switch
        {
            AcceptInvitationResult.Ok ok => Results.Ok(new AcceptInvitationResponse(ok.Email)),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.Validation } => Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Password or display name invalid",
                detail: "Password must be 12–128 characters (complexity enforced by the identity provider). Display name must be non-empty.",
                statusCode: StatusCodes.Status400BadRequest),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.NotFound } => Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Invitation not found",
                detail: "No invitation matches the supplied token.",
                statusCode: StatusCodes.Status404NotFound),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.Upstream } => Results.Problem(
                type: ProblemTypes.ServiceUnavailable,
                title: "Identity provider error",
                detail: "The identity provider rejected the account creation. Please retry.",
                statusCode: StatusCodes.Status502BadGateway),
            AcceptInvitationResult.Failed => Results.Problem(
                type: ProblemTypes.InvitationGone,
                title: "Invitation no longer valid",
                detail: "The invitation has expired, been revoked, or was already accepted.",
                statusCode: StatusCodes.Status410Gone),
            _ => Results.Problem(
                type: ProblemTypes.InternalServerError,
                title: "Unexpected error",
                statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    // Token rides in the URL query string, so suppress the Referer header (spec D5 token-leak mitigation).
    private static void NoReferrer(HttpContext ctx) =>
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
}
