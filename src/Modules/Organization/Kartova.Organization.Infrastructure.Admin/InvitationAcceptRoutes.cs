using Kartova.Organization.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Organization.Infrastructure.Admin;

internal static class InvitationAcceptRoutes
{
    public const string RateLimitPolicy = "invitation-accept";

    public static void MapTo(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/invitations")
            .RequireRateLimiting(RateLimitPolicy);

        group.MapGet("/accept", GetContextAsync)
            .WithName("GetInvitationAcceptContext")
            .Produces<InvitationAcceptContext>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/accept", AcceptAsync)
            .WithName("AcceptInvitation")
            .Produces<AcceptInvitationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);
    }

    private static async Task<IResult> GetContextAsync(
        string token,
        [FromServices] AcceptInvitationHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        var result = await handler.GetContextAsync(token, ct);
        return result switch
        {
            GetAcceptContextResult.Ok ok => Results.Ok(ok.Context),
            GetAcceptContextResult.Failed { Error: AcceptInvitationError.NotFound } => Results.NotFound(),
            GetAcceptContextResult.Failed f => Results.Problem(statusCode: StatusCodes.Status410Gone, title: f.Error.ToString()),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> AcceptAsync(
        AcceptInvitationRequest body,
        [FromServices] AcceptInvitationHandler handler,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        var result = await handler.AcceptAsync(body.Token, body.Password, body.DisplayName, ct);
        return result switch
        {
            AcceptInvitationResult.Ok ok => Results.Ok(new AcceptInvitationResponse(ok.Email)),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.Validation } => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Password or display name invalid."),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.NotFound } => Results.NotFound(),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.Upstream } => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Identity provider error."),
            AcceptInvitationResult.Failed f => Results.Problem(statusCode: StatusCodes.Status410Gone, title: f.Error.ToString()),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
