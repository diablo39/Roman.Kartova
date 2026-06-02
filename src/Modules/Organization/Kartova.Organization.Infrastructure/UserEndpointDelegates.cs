using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Endpoint delegates for the User surface (slice 9 spec §6.7) under
/// <c>/api/v1/organizations/users</c>: typeahead search and detail. Both are
/// read-only projections against the local <c>users</c> table joined with
/// <c>team_members</c> for the detail surface. Split out of the legacy
/// <c>OrganizationEndpointDelegates</c> aggregator (slice-9 carry-forward #16) —
/// behavior is identical, only the host type name changed.
/// </summary>
internal static class UserEndpointDelegates
{
    /// <summary>
    /// <c>GET /users?q=...&amp;limit=...</c>: typeahead search across the
    /// current tenant's users projection. <c>q</c> must be at least 2
    /// characters — body-shape validation surfaces a 422 envelope. <c>limit</c>
    /// is clamped to [1, 20] inside <see cref="UserQueries.SearchAsync"/>.
    /// Returns a bounded list (no cursor envelope) — see CLAUDE.md "bounded
    /// list" rule; the underlying query class is not a <c>List*Handler</c>,
    /// so <c>PaginationConventionRules</c> does not require
    /// <c>[BoundedListResult]</c>.
    /// </summary>
    internal static async Task<IResult> SearchUsersAsync(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        UserQueries queries,
        CancellationToken ct)
    {
        // Trim before validating so a query like "  a  " is treated as a 1-char
        // query (and rejected with "too short"), not a 5-char query that searches
        // for a literal "  a  " substring (which would degrade to zero matches).
        var trimmed = q?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid search query",
                detail: "Query 'q' is required.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        if (trimmed.Length < 2)
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid search query",
                detail: "Query 'q' must be at least 2 characters.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var rows = await queries.SearchAsync(trimmed, limit ?? 20, ct);
        return Results.Ok(rows);
    }

    /// <summary>
    /// <c>GET /users/{id}</c>: returns the user's profile plus team
    /// memberships scoped to the current tenant (RLS-filtered). 404 surfaces
    /// the same envelope whether the id is unknown or visible only in another
    /// tenant (ADR-0090 — RLS hides the difference).
    /// </summary>
    internal static async Task<IResult> GetUserDetailAsync(
        Guid id,
        UserQueries queries,
        CancellationToken ct)
    {
        var user = await queries.GetDetailAsync(id, ct);
        if (user is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "User not found",
                detail: "No user with that id is visible in the current tenant.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(user);
    }
}

/// <summary>
/// Route composition for the User surface (`/users`, `/users/{id}`). Slice 9
/// spec §6.7. Extracted from <c>OrganizationModule.MapEndpoints</c> in
/// slice-9 carry-forward S6.
/// Typeahead search is a bounded list (no cursor envelope) — limit is clamped
/// at 20 inside <see cref="UserQueries.SearchAsync"/>.
/// </summary>
internal static class UserRoutes
{
    public static void MapTo(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/users", UserEndpointDelegates.SearchUsersAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersSearch)
            .WithName("SearchUsers")
            .Produces<IReadOnlyList<UserSummaryResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        tenant.MapGet("/users/{id:guid}", UserEndpointDelegates.GetUserDetailAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRead)
            .WithName("GetUserDetail")
            .Produces<UserDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
