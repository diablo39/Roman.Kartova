using System.Security.Claims;
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal static class OrganizationEndpointDelegates
{
    internal static async Task<IResult> GetMeAsync(OrgProfileQueries queries, CancellationToken ct)
    {
        var profile = await queries.GetMyOrgAsync(ct);
        if (profile is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization not found",
                detail: "The current tenant has no visible Organization row.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(profile);
    }

    /// <summary>
    /// Slice-9 spec §4: applies the supplied <see cref="UpdateOrgProfileRequest"/>
    /// to the current tenant's <c>Organization</c>. The If-Match header is
    /// reserved on the wire contract but not parsed here — see the inline note
    /// inside the body for the deferral rationale.
    /// </summary>
    internal static async Task<IResult> UpdateMeAsync(
        [FromBody] UpdateOrgProfileRequest body,
        UpdateOrgProfileHandler handler,
        CancellationToken ct)
    {
        // If-Match wire contract reserved per slice-9 spec §4 + ADR-0096.
        // The Organization aggregate does not carry an EF concurrency token yet,
        // so the header would be a no-op even if parsed. When xmin mapping lands,
        // wire IfMatchEndpointFilter (already used by CatalogModule) onto this
        // endpoint and pull the expected token from HttpContext.Items rather than
        // re-implementing header parsing in the delegate.
        var result = await handler.HandleAsync(body, ifMatch: null, ct);
        return result switch
        {
            UpdateOrgProfileResult.Ok => Results.NoContent(),
            UpdateOrgProfileResult.NotFound => Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization not found",
                detail: "The current tenant has no visible Organization row.",
                statusCode: StatusCodes.Status404NotFound),
            UpdateOrgProfileResult.ConcurrencyConflict => Results.Problem(
                type: ProblemTypes.ConcurrencyConflict,
                title: "Concurrency conflict",
                detail: "The Organization row was modified by a concurrent request.",
                statusCode: StatusCodes.Status412PreconditionFailed),
            _ => Results.StatusCode(500),
        };
    }

    internal static IResult GetAdminOnlyAsync()
    {
        return Results.Ok(new AdminOnlyResponse("ok"));
    }

    internal static IResult GetMePermissions(ICurrentUser currentUser, ClaimsPrincipal user)
    {
        // Spec §3 Decision #2: each user holds exactly one realm role.
        // FirstOrDefault is the explicit choice — if multiple ClaimTypes.Role
        // claims somehow arrive on the principal, only the first is surfaced.
        var role = user.FindAll(ClaimTypes.Role)
                       .Select(c => c.Value)
                       .FirstOrDefault();

        var permissions = user.FindAll(KartovaClaims.Permission)
                              .Select(c => c.Value)
                              .ToArray();

        // Slice 8 §7.2: surface the caller's team memberships so the SPA can
        // gate team-admin-of-this UI without a second round-trip. Role is the
        // string form of TeamRoleKind ("Admin" | "Member"), matching the
        // TeamMemberResponse wire shape.
        var memberships = currentUser.TeamMemberships
            .Select(m => new MeTeamMembership(m.TeamId, m.Role.ToString()))
            .ToArray();

        return Results.Ok(new MePermissionsResponse(role, permissions, memberships));
    }

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
        TeamSortField? parsedSortBy = null;
        if (sortBy is not null)
        {
            if (!Enum.TryParse<TeamSortField>(sortBy, ignoreCase: true, out var sf)
                || !Enum.IsDefined(sf))
            {
                throw new InvalidSortFieldException(sortBy, TeamSortSpecs.AllowedFieldNames);
            }
            parsedSortBy = sf;
        }

        SortOrder? parsedSortOrder = null;
        if (sortOrder is not null)
        {
            if (!Enum.TryParse<SortOrder>(sortOrder, ignoreCase: true, out var so)
                || !Enum.IsDefined(so))
            {
                throw new InvalidSortOrderException(sortOrder);
            }
            parsedSortOrder = so;
        }

        int effectiveLimit;
        if (limit is null)
        {
            effectiveLimit = QueryablePagingExtensions.DefaultLimit;
        }
        else if (!int.TryParse(limit, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out effectiveLimit))
        {
            throw new InvalidLimitException(
                limit,
                QueryablePagingExtensions.MinLimit,
                QueryablePagingExtensions.MaxLimit);
        }

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
        var resp = new TeamMemberResponse(request.UserId, role.ToString(), result.AddedAt!.Value);
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
