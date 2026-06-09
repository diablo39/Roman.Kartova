using System.Security.Claims;
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Endpoint delegates for the Team surface under
/// <c>/api/v1/organizations/teams</c>: list / get / create / update / delete
/// plus the team-member add / remove / update triplet. All mutation endpoints
/// run the shared <see cref="LoadAndAuthorizeTeamAsync"/> gate
/// (<c>TeamAdminOfThis</c> resource policy) before invoking the handler. Split
/// out of the legacy <c>OrganizationEndpointDelegates</c> aggregator (slice-9
/// carry-forward #16) — behavior is identical, only the host type name changed.
/// </summary>
internal static class TeamEndpointDelegates
{
    // ----- Teams: list / get / create -----------------------------------

    /// <summary>
    /// Mirrors <c>CatalogEndpointDelegates.ListApplicationsAsync</c>: <c>sortBy</c>
    /// / <c>sortOrder</c> bind as nullable strings, are parsed via
    /// <c>Enum.TryParse(ignoreCase: true)</c> + <c>Enum.IsDefined</c>, and invalid
    /// inputs throw the same paging exceptions the shared <c>PagingExceptionHandler</c>
    /// converts to RFC 7807 400s. Keeps the wire envelope identical across
    /// resources (ADR-0095 §4.3).
    /// </summary>
    internal static async Task<IResult> ListTeamsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        ListTeamsHandler handler,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var (parsedSortBy, parsedSortOrder, effectiveLimit) = CursorListBinding.Bind<TeamSortField>(
            sortBy, sortOrder, limit, TeamSortSpecs.AllowedFieldNames);

        var query = new ListTeamsQuery(
            SortBy: parsedSortBy ?? TeamSortField.CreatedAt,
            SortOrder: parsedSortOrder ?? SortOrder.Desc,
            Cursor: cursor,
            Limit: effectiveLimit);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }

    internal static async Task<IResult> GetTeamAsync(
        Guid id,
        GetTeamHandler handler,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(new GetTeamQuery(id), db, ct);
        if (resp is null) return TeamNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> CreateTeamAsync(
        [FromBody] CreateTeamRequest request,
        CreateTeamHandler handler,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(
            new CreateTeamCommand(request.DisplayName, request.Description), db, ct);
        return Results.Created($"/api/v1/organizations/teams/{resp.Id}", resp);
    }

    // ----- Teams: mutate (team-admin-of-this gated) ---------------------

    internal static async Task<IResult> UpdateTeamAsync(
        Guid id,
        [FromBody] UpdateTeamRequest request,
        UpdateTeamHandler handler,
        OrganizationDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeTeamAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        var resp = await handler.Handle(
            new UpdateTeamCommand(id, request.DisplayName, request.Description), db, ct);
        // Defensive 404: handler returns null only on missing team, but we already
        // loaded and authorized above — if it slips through (e.g. concurrent delete),
        // surface the same 404 envelope clients expect.
        if (resp is null) return TeamNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> DeleteTeamAsync(
        Guid id,
        DeleteTeamHandler handler,
        OrganizationDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeTeamAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        var result = await handler.Handle(new DeleteTeamCommand(id), db, ct);
        if (result.NotFound) return TeamNotFound();
        if (result.ApplicationsAssigned is > 0)
        {
            // 409 with applicationCount extension — the SPA renders
            // "{N} applications still assigned" in its toast (spec §6.5 / §6.x).
            return Results.Problem(
                type: ProblemTypes.TeamHasApplications,
                title: "Team has assigned applications",
                detail: $"Cannot delete team: {result.ApplicationsAssigned} application(s) are still assigned. Reassign or unassign them first.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["applicationCount"] = result.ApplicationsAssigned });
        }
        return Results.NoContent();
    }

    // ----- Team members (team-admin-of-this gated) ----------------------

    internal static async Task<IResult> AddTeamMemberAsync(
        Guid id,
        [FromBody] AddTeamMemberRequest request,
        AddTeamMemberHandler handler,
        OrganizationDbContext db,
        IAuthorizationService auth,
        IUserDirectory directory,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeTeamAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        if (!TryParseRole(request.Role, out var role, out var roleError)) return roleError;

        var result = await handler.Handle(new AddTeamMemberCommand(id, request.UserId, role), db, ct);
        if (result.TeamNotFound) return TeamNotFound();
        if (result.AlreadyMember)
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Duplicate membership",
                detail: "User is already a member of this team.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Spec §critic-revision item 7: AddTeamMember returns 201 + TeamMemberResponse,
        // NOT 204. AddedAt is the canonical value the handler persisted on the
        // aggregate — surfaced via AddTeamMemberResult — so clients see exactly
        // the timestamp the DB sees, not a re-clocked wall-clock snapshot taken
        // here (slice-boundary review fix item 6).
        //
        // Slice 9 / E3 (ADR-0098): enrich the response with the new member's
        // DisplayName + Email so the SPA can immediately render the row without
        // a follow-up users lookup. The IUserDirectory port shares an
        // OrganizationDbContext with the handler, so the read sees the just-
        // committed users projection row when KeyCloak post-auth has run for
        // this user. When no row is visible (cross-tenant lag or unimported
        // user), both fields fall back to "" per the contract docstring.
        var info = await directory.GetAsync(request.UserId, ct);
        var resp = new TeamMemberResponse(
            request.UserId,
            role.ToString(),
            result.AddedAt!.Value,
            info?.DisplayName ?? "",
            info?.Email ?? "");
        return Results.Created($"/api/v1/organizations/teams/{id}/members/{request.UserId}", resp);
    }

    internal static async Task<IResult> RemoveTeamMemberAsync(
        Guid id,
        Guid userId,
        RemoveTeamMemberHandler handler,
        OrganizationDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeTeamAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        var result = await handler.Handle(new RemoveTeamMemberCommand(id, userId), db, ct);
        if (result.TeamNotFound) return TeamNotFound();
        if (result.MemberNotFound)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Membership not found",
                detail: "No membership exists for this user on this team.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.NoContent();
    }

    internal static async Task<IResult> UpdateTeamMemberAsync(
        Guid id,
        Guid userId,
        [FromBody] UpdateTeamMemberRequest request,
        UpdateTeamMemberHandler handler,
        OrganizationDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeTeamAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        if (!TryParseRole(request.Role, out var role, out var roleError)) return roleError;

        var result = await handler.Handle(new UpdateTeamMemberCommand(id, userId, role), db, ct);
        if (result.TeamNotFound) return TeamNotFound();
        if (result.MemberNotFound)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Membership not found",
                detail: "No membership exists for this user on this team.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.NoContent();
    }

    // ----- shared helpers -----------------------------------------------

    /// <summary>
    /// Loads the team by id (RLS-scoped to the current tenant) and runs the
    /// <see cref="KartovaTeamPolicies.TeamAdminOfThis"/> resource gate against
    /// it. Returns <c>null</c> on success; otherwise returns the response to
    /// short-circuit with (404 if the team is not visible, 403 if the caller
    /// is not a team admin of it). Used by every mutation endpoint on
    /// <c>/teams/{id}</c> and <c>/teams/{id}/members/...</c>.
    /// </summary>
    private static async Task<IResult?> LoadAndAuthorizeTeamAsync(
        Guid id,
        OrganizationDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(id), ct);
        if (team is null) return TeamNotFound();

        var authResult = await auth.AuthorizeAsync(user, team, KartovaTeamPolicies.TeamAdminOfThis);
        if (!authResult.Succeeded) return Results.Forbid();

        return null;
    }

    /// <summary>
    /// Parses a wire-format role string ("Admin" / "Member", case-insensitive)
    /// into the strongly-typed <see cref="TeamRole"/>. Returns <c>true</c> on
    /// success; on failure sets <paramref name="error"/> to the RFC 7807 400
    /// envelope clients receive and returns <c>false</c>. Shared by AddMember
    /// and UpdateMember which both bind <c>request.Role</c> as a string.
    /// </summary>
    private static bool TryParseRole(string raw, out TeamRole role, out IResult error)
    {
        if (Enum.TryParse(raw, ignoreCase: true, out role) && Enum.IsDefined(role))
        {
            error = null!;
            return true;
        }

        error = Results.Problem(
            type: ProblemTypes.ValidationFailed,
            title: "Invalid role",
            detail: $"'{raw}' must be 'Admin' or 'Member'.",
            statusCode: StatusCodes.Status400BadRequest);
        return false;
    }

    /// <summary>
    /// RFC 7807 404 envelope shared by every Team endpoint that resolves a
    /// team by id. RLS hides cross-tenant rows so unknown id and cross-tenant
    /// id surface identically (intentional, ADR-0090). Mirrors
    /// <c>EndpointResultExtensions.ApplicationNotFound</c> in the Catalog module.
    /// </summary>
    private static IResult TeamNotFound() => Results.Problem(
        type: ProblemTypes.ResourceNotFound,
        title: "Team not found",
        detail: "No team with that id is visible in the current tenant.",
        statusCode: StatusCodes.Status404NotFound);
}

/// <summary>
/// Route composition for the Team surface (`/teams`, `/teams/{id}`,
/// `/teams/{id}/members`, `/teams/{id}/members/{userId}`). Slice 8 / ADR-0098,
/// spec §6.5–§10. Extracted from <c>OrganizationModule.MapEndpoints</c> in
/// slice-9 carry-forward S6.
/// <para>
/// Read endpoints (<c>team.read</c>) and CreateTeam (<c>team.create</c>) gate on a
/// claim policy via <c>RequireAuthorization(permission)</c>. The five mutation
/// endpoints (ADR-0101) drop the claim gate to a bare <c>RequireAuthorization()</c>
/// (authenticated + tenant baseline) and authorize solely on the inline
/// <c>TeamAdminOfThis</c> resource policy enforced inside the delegate via
/// <see cref="IAuthorizationService"/> — team-admin authority is per-team
/// <c>Admin</c> membership, not a realm role. CreateTeam stays OrgAdmin-only as it
/// has no target team yet.
/// </para>
/// </summary>
internal static class TeamRoutes
{
    public static void MapTo(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/teams", TeamEndpointDelegates.ListTeamsAsync)
            .RequireAuthorization(KartovaPermissions.TeamRead)
            .WithName("ListTeams")
            // CursorPage<T> envelope — ADR-0095: items + nextCursor + prevCursor.
            // sortBy/sortOrder enum schemas + bounded-integer limit schema are emitted
            // by the OpenAPI transformer wired in Program.cs (same path as catalog).
            .Produces<CursorPage<TeamResponse>>(StatusCodes.Status200OK);

        tenant.MapGet("/teams/{id:guid}", TeamEndpointDelegates.GetTeamAsync)
            .RequireAuthorization(KartovaPermissions.TeamRead)
            .WithName("GetTeam")
            .Produces<TeamDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapPost("/teams", TeamEndpointDelegates.CreateTeamAsync)
            .RequireAuthorization(KartovaPermissions.TeamCreate)
            .WithName("CreateTeam")
            .Produces<TeamResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Mutation routes authorize via the inline TeamAdminOfThis resource gate
        // (LoadAndAuthorizeTeamAsync), not a claim policy — team-admin authority is
        // per-team Admin membership (ADR-0101). RequireAuthorization() keeps the
        // authenticated+tenant baseline so anonymous callers still get 401.
        tenant.MapPut("/teams/{id:guid}", TeamEndpointDelegates.UpdateTeamAsync)
            .RequireAuthorization()
            .WithName("UpdateTeam")
            .Produces<TeamResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapDelete("/teams/{id:guid}", TeamEndpointDelegates.DeleteTeamAsync)
            .RequireAuthorization()
            .WithName("DeleteTeam")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            // 409 team-has-applications with applicationCount extension — spec §6.5.
            .ProducesProblem(StatusCodes.Status409Conflict);

        tenant.MapPost("/teams/{id:guid}/members", TeamEndpointDelegates.AddTeamMemberAsync)
            .RequireAuthorization()
            .WithName("AddTeamMember")
            // 201 + TeamMemberResponse body (spec critic-revision §7 — NOT 204).
            .Produces<TeamMemberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        tenant.MapDelete("/teams/{id:guid}/members/{userId:guid}", TeamEndpointDelegates.RemoveTeamMemberAsync)
            .RequireAuthorization()
            .WithName("RemoveTeamMember")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapPut("/teams/{id:guid}/members/{userId:guid}", TeamEndpointDelegates.UpdateTeamMemberAsync)
            .RequireAuthorization()
            .WithName("UpdateTeamMember")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
