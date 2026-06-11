using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
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
    /// <c>GET /users</c>: cursor-paginated members directory for the current
    /// tenant. Supports optional <c>role</c> (narrows by realm role) and
    /// <c>q</c> (infix on display_name + email) filters. Requires
    /// <c>org.users.read</c> — Viewer and above. ADR-0095.
    /// </summary>
    internal static async Task<IResult> ListMembersAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] string? role,
        [FromQuery] string? q,
        ListMembersHandler handler,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        // Mirror the typeahead's min-length floor: a non-null q shorter than 2
        // chars can't use the trigram GIN index and is almost always a mistake.
        // null/empty/whitespace q is still allowed and means "no filter".
        var trimmedQ = q?.Trim();
        if (trimmedQ is { Length: > 0 and < 2 })
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid search query",
                detail: "Query 'q' must be at least 2 characters.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Normalize the role filter: accept the documented camelCase/lowercase variants
        // (viewer, member, orgAdmin) and resolve them to the PascalCase canonical stored
        // values (Viewer, Member, OrgAdmin). Unknown values → 422 so clients get precise
        // feedback instead of a silent empty page. null/empty/whitespace/"all" means
        // "no filter" and passes null into the query (handler already treats null/"all"
        // identically). StringComparison.OrdinalIgnoreCase handles every casing variant.
        string? canonicalRole = null;
        var trimmedRole = role?.Trim();
        if (!string.IsNullOrEmpty(trimmedRole)
            && !string.Equals(trimmedRole, "all", StringComparison.OrdinalIgnoreCase))
        {
            canonicalRole = KartovaRoles.All.FirstOrDefault(
                r => string.Equals(r, trimmedRole, StringComparison.OrdinalIgnoreCase));
            if (canonicalRole is null)
            {
                return Results.Problem(
                    type: ProblemTypes.ValidationFailed,
                    title: "Invalid role filter",
                    detail: $"role must be one of: {string.Join(", ", KartovaRoles.All)}, or 'all'.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }

        var (parsedSortBy, parsedSortOrder, effectiveLimit) = CursorListBinding.Bind<MemberSortField>(
            sortBy, sortOrder, limit, MemberSortSpecs.AllowedFieldNames);

        var query = new ListMembersQuery(
            SortBy: parsedSortBy ?? MemberSortField.DisplayName,
            SortOrder: parsedSortOrder ?? SortOrder.Asc,
            Cursor: cursor,
            Limit: effectiveLimit,
            Role: canonicalRole,
            Q: string.IsNullOrEmpty(trimmedQ) ? null : trimmedQ);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
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

    /// <summary>
    /// <c>PUT /users/{id}/role</c>: OrgAdmin changes a member's realm role
    /// (Viewer / Member / OrgAdmin). KeyCloak is the source of truth; the
    /// <c>users.realm_role</c> projection column is a write-through cache
    /// (ADR-0102 / slice-10 Task 5). Guard: cannot demote the last OrgAdmin.
    /// </summary>
    internal static async Task<IResult> ChangeMemberRoleAsync(
        Guid id, [FromBody] UpdateMemberRoleRequest request,
        ChangeMemberRoleHandler handler, OrganizationDbContext db, CancellationToken ct)
    {
        var result = await handler.Handle(new ChangeMemberRoleCommand(id, request.Role), db, ct);
        if (result.InvalidRole)
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid role",
                detail: $"Role must be one of: {string.Join(", ", KartovaRoles.All)}.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        if (result.NotFound)
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Member not found",
                detail: $"No member with id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        if (result.LastOrgAdmin)
            return Results.Problem(
                type: ProblemTypes.LastOrgAdmin,
                title: "Cannot demote the last OrgAdmin",
                detail: "The organization must retain at least one OrgAdmin.",
                statusCode: StatusCodes.Status409Conflict);
        return Results.NoContent();
    }

    /// <summary>
    /// <c>DELETE /users/{id}</c>: OrgAdmin offboards a member (slice-10 Task 6,
    /// spec §6.7). Deletes the KeyCloak identity, cascade-removes the member's
    /// team memberships, and deletes the local <c>users</c> projection row.
    /// Application.CreatedByUserId is immutable history — no ownership reassignment
    /// occurs (ADR-0102 update). Guards: not-found 404, cannot-offboard-self 409,
    /// last-OrgAdmin 409. The acting (caller's) user id comes from
    /// <see cref="ICurrentUser.UserId"/> so the self-offboard guard compares against
    /// the authenticated principal.
    /// </summary>
    internal static async Task<IResult> OffboardMemberAsync(
        Guid id, OffboardMemberHandler handler, OrganizationDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var result = await handler.Handle(
            new OffboardMemberCommand(new OffboardTargetUserId(id), new OffboardActingUserId(currentUser.UserId)), db, ct);
        if (result.NotFound)
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound, title: "Member not found",
                detail: $"No member with id {id}.", statusCode: StatusCodes.Status404NotFound);
        if (result.CannotOffboardSelf)
            return Results.Problem(
                type: ProblemTypes.CannotOffboardSelf, title: "Cannot offboard yourself",
                detail: "You cannot remove your own membership.", statusCode: StatusCodes.Status409Conflict);
        if (result.LastOrgAdmin)
            return Results.Problem(
                type: ProblemTypes.LastOrgAdmin, title: "Cannot offboard the last OrgAdmin",
                detail: "The organization must retain at least one OrgAdmin.", statusCode: StatusCodes.Status409Conflict);
        return Results.NoContent();
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
        tenant.MapGet("/users", UserEndpointDelegates.ListMembersAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRead)
            .WithName("ListMembers")
            .Produces<CursorPage<MemberSummaryResponse>>(StatusCodes.Status200OK);

        tenant.MapGet("/users/search", UserEndpointDelegates.SearchUsersAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersSearch)
            .WithName("SearchUsers")
            .Produces<IReadOnlyList<UserSummaryResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        tenant.MapGet("/users/{id:guid}", UserEndpointDelegates.GetUserDetailAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRead)
            .WithName("GetUserDetail")
            .Produces<UserDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapPut("/users/{id:guid}/role", UserEndpointDelegates.ChangeMemberRoleAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRoleChange)
            .WithName("ChangeMemberRole")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        tenant.MapDelete("/users/{id:guid}", UserEndpointDelegates.OffboardMemberAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRemove)
            .WithName("OffboardMember")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
