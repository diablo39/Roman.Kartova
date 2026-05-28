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
using Microsoft.Net.Http.Headers;

namespace Kartova.Organization.Infrastructure;

internal static class OrganizationEndpointDelegates
{
    // Must match the OrgLogo.Create invariant
    // (src/Modules/Organization/Kartova.Organization.Domain/OrgLogo.cs:22).
    // The aggregate enforces <= 256 KiB; the endpoint enforces the same limit
    // early so a hostile uploader can't stream gigabytes before the domain
    // validation kicks in.
    private const int LogoMaxBytes = 256 * 1024;

    // Read buffer for the upload stream. 8 KiB matches Kestrel's default
    // request-body buffer and is well below the LogoMaxBytes ceiling.
    private const int LogoUploadReadBuffer = 8192;
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

    // ----- Logo upload / clear / serve (slice 9 spec §6.4) --------------

    /// <summary>
    /// PUT <c>/me/logo</c>: streams the request body (capped at
    /// <see cref="LogoMaxBytes"/>), validates content-type + magic bytes,
    /// sanitizes SVG, and persists via <see cref="LogoCommands.UploadAsync"/>.
    /// Returns 200 + <see cref="UploadLogoResponse"/> on success, RFC 7807
    /// envelopes for 413/415/422/404. Content-Type is parsed via
    /// <see cref="MediaTypeHeaderValue"/> so <c>image/PNG</c> and
    /// <c>image/png; charset=utf-8</c> both normalize correctly.
    /// </summary>
    internal static async Task<IResult> UploadLogoAsync(
        HttpRequest req,
        LogoCommands cmds,
        CancellationToken ct)
    {
        var rawContentType = req.Headers.ContentType.ToString();
        string? mediaType = null;
        if (MediaTypeHeaderValue.TryParse(rawContentType, out var mt))
        {
            mediaType = mt.MediaType.Value?.ToLowerInvariant();
        }

        if (mediaType is not ("image/png" or "image/jpeg" or "image/svg+xml"))
        {
            return Results.Problem(
                type: ProblemTypes.UnsupportedLogoMedia,
                title: "Unsupported logo media type",
                detail: "Content-Type must be image/png, image/jpeg, or image/svg+xml.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        // Stream the body with an explicit size ceiling rather than trusting
        // Content-Length (which a hostile client can spoof or omit).
        using var ms = new MemoryStream();
        var buffer = new byte[LogoUploadReadBuffer];
        int read;
        while ((read = await req.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > LogoMaxBytes)
            {
                return Results.Problem(
                    type: ProblemTypes.UnsupportedLogoMedia,
                    title: "Logo too large",
                    detail: $"Logo bytes must be <= {LogoMaxBytes:N0} bytes.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }
        }
        var bytes = ms.ToArray();

        var result = await cmds.UploadAsync(bytes, mediaType, ct);
        return result switch
        {
            UploadLogoResult.Accepted a => Results.Ok(new UploadLogoResponse(a.Etag, a.MimeType)),
            UploadLogoResult.Rejected r => Results.Problem(
                type: ProblemTypes.UnsupportedLogoMedia,
                title: "Logo rejected",
                detail: r.Reason,
                statusCode: StatusCodes.Status422UnprocessableEntity),
            UploadLogoResult.NotFound => Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization not found",
                detail: "The current tenant has no visible Organization row.",
                statusCode: StatusCodes.Status404NotFound),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// DELETE <c>/me/logo</c>: clears the logo on the current tenant's
    /// Organization aggregate. 204 on success, RFC 7807 404 when no
    /// Organization is visible (RLS or missing row).
    /// </summary>
    internal static async Task<IResult> DeleteLogoAsync(
        LogoCommands cmds,
        CancellationToken ct)
    {
        var ok = await cmds.ClearAsync(ct);
        if (!ok)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization not found",
                detail: "The current tenant has no visible Organization row.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.NoContent();
    }

    /// <summary>
    /// GET <c>/me/logo</c>: serves the raw logo bytes with a strong ETag
    /// (SHA-256 hex of the bytes) and <c>Cache-Control: private, max-age=300</c>.
    /// Honors <c>If-None-Match</c> for 304 short-circuit. This is the cache-
    /// validation header (RFC 7232 §3.2), distinct from <c>If-Match</c> which
    /// is used for optimistic concurrency on the profile PUT.
    /// </summary>
    internal static async Task<IResult> GetLogoAsync(
        LogoCommands cmds,
        HttpContext http,
        CancellationToken ct)
    {
        var data = await cmds.GetServeDataAsync(ct);
        if (data is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization logo not found",
                detail: "No logo is set on the current tenant's Organization.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // RFC 7232: parse If-None-Match via EntityTagHeaderValue so we correctly
        // handle quoted strong validators ("hash") and reject weak validators
        // (W/"hash"). The server only emits strong ETags, so a weak If-None-Match
        // cannot match anything we store and must fall through to a 200.
        string? ifNoneMatch = null;
        var rawIfNoneMatch = http.Request.Headers.IfNoneMatch.FirstOrDefault();
        if (!string.IsNullOrEmpty(rawIfNoneMatch)
            && EntityTagHeaderValue.TryParse(rawIfNoneMatch, out var etag)
            && !etag.IsWeak)
        {
            // EntityTagHeaderValue.Tag includes the surrounding quotes
            // (e.g. "abc123"); strip them to match the raw ContentHash hex.
            ifNoneMatch = etag.Tag.Value?.Trim('"');
        }

        // ETag is the validator; Cache-Control gives downstream caches freshness.
        // Both must be present on 304 and 200 per RFC 7232 §4.1, otherwise an
        // intermediate cache won't extend its freshness window after a successful
        // revalidation. Set both before branching so neither path can forget one.
        http.Response.Headers.ETag = $"\"{data.ContentHash}\"";
        http.Response.Headers.CacheControl = "private, max-age=300";

        // Defence-in-depth on the SVG render path. D3's allow-list sanitizer is the
        // primary defence (no <script>, no event handlers, no http(s) hrefs, only
        // data: scheme). These two headers cover the case where an attacker bypasses
        // the sanitizer AND a user opens /me/logo directly in a browser tab (where
        // SVG renders as a *document* and inline JS would execute — unlike <img src>
        // usage which never executes SVG scripts).
        //   • CSP sandbox + default-src 'none' blocks JS execution regardless of
        //     load mode; style-src 'unsafe-inline' keeps inline SVG presentation
        //     attributes working.
        //   • X-Content-Type-Options: nosniff prevents content-type sniffing from
        //     reinterpreting the response as HTML/JS.
        http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        http.Response.Headers.XContentTypeOptions = "nosniff";

        if (ifNoneMatch is not null && ifNoneMatch == data.ContentHash)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.File(data.Bytes, data.MimeType);
    }

    // ----- Invitations (slice 9 spec §6.7) ------------------------------

    /// <summary>
    /// <c>GET /invitations</c>: cursor-paginated list of invitations visible
    /// to the current tenant (RLS-filtered). Optional <c>status</c> filter
    /// narrows to a single <see cref="InvitationStatus"/>. Parsing mirrors
    /// <see cref="ListTeamsAsync"/> — same paging exceptions translate to
    /// RFC 7807 400 envelopes via <c>PagingExceptionHandler</c>.
    /// </summary>
    internal static async Task<IResult> ListInvitationsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] string? status,
        ListInvitationsHandler handler,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        InvitationSortField? parsedSortBy = null;
        if (sortBy is not null)
        {
            if (!Enum.TryParse<InvitationSortField>(sortBy, ignoreCase: true, out var sf)
                || !Enum.IsDefined(sf))
            {
                throw new InvalidSortFieldException(sortBy, InvitationSortSpecs.AllowedFieldNames);
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

        InvitationStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<InvitationStatus>(status, ignoreCase: true, out var s)
                || !Enum.IsDefined(s))
            {
                return Results.Problem(
                    type: ProblemTypes.ValidationFailed,
                    title: "Invalid status filter",
                    detail: $"'{status}' must be one of: Pending, Accepted, Revoked, Expired.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            parsedStatus = s;
        }

        var query = new ListInvitationsQuery(
            SortBy: parsedSortBy ?? InvitationSortField.InvitedAt,
            SortOrder: parsedSortOrder ?? SortOrder.Desc,
            Cursor: cursor,
            Limit: effectiveLimit,
            StatusFilter: parsedStatus);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }

    /// <summary>
    /// <c>POST /invitations</c>: creates a new invitation. Maps the handler's
    /// failure taxonomy to RFC 7807 envelopes — three-way 409s for the email
    /// conflict matrix, 422 for input validation, 502 for upstream KeyCloak
    /// failures. Spec §6.7.
    /// </summary>
    internal static async Task<IResult> CreateInvitationAsync(
        [FromBody] CreateInvitationRequest body,
        CreateInvitationHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(body, ct);
        return result switch
        {
            CreateInvitationResult.Created c => Results.Created(
                $"/api/v1/organizations/invitations/{c.Response.Invitation.Id}",
                c.Response),
            CreateInvitationResult.Failed f => f.Error switch
            {
                CreateInvitationError.Validation => Results.Problem(
                    type: ProblemTypes.ValidationFailed,
                    title: "Invalid invitation request",
                    detail: "Email must be a non-empty, <=320-character string containing '@', and role must be one of: Viewer, Member, TeamAdmin, OrgAdmin.",
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                CreateInvitationError.EmailAlreadyInTenant => Results.Problem(
                    type: ProblemTypes.EmailAlreadyInTenant,
                    title: "Email already in this tenant",
                    detail: "A user with this email is already a member of the current tenant.",
                    statusCode: StatusCodes.Status409Conflict),
                CreateInvitationError.EmailAlreadyInvited => Results.Problem(
                    type: ProblemTypes.EmailAlreadyInvited,
                    title: "Email already invited",
                    detail: "A pending invitation for this email already exists in the current tenant.",
                    statusCode: StatusCodes.Status409Conflict),
                CreateInvitationError.EmailAlreadyOnPlatform => Results.Problem(
                    type: ProblemTypes.EmailAlreadyOnPlatform,
                    title: "Email already on platform",
                    detail: "A KeyCloak account already exists for this email outside the current tenant.",
                    statusCode: StatusCodes.Status409Conflict),
                CreateInvitationError.Upstream => Results.Problem(
                    type: ProblemTypes.ServiceUnavailable,
                    title: "Upstream KeyCloak error",
                    detail: "Could not assign the realm role on KeyCloak. The KeyCloak user was rolled back; please retry.",
                    statusCode: StatusCodes.Status502BadGateway),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            },
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// <c>POST /invitations/{id}/revoke</c>: revokes a pending invitation.
    /// 204 on success; 404 when the id is not visible in the current tenant
    /// (RLS or unknown); 409 when the invitation is not in <c>Pending</c>
    /// state. Spec §6.7.
    /// </summary>
    internal static async Task<IResult> RevokeInvitationAsync(
        Guid id,
        RevokeInvitationHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(id, ct);
        return result switch
        {
            RevokeResult.Ok => Results.NoContent(),
            RevokeResult.NotFound => Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Invitation not found",
                detail: "No invitation with that id is visible in the current tenant.",
                statusCode: StatusCodes.Status404NotFound),
            RevokeResult.NotPending => Results.Problem(
                type: ProblemTypes.InvitationNotPending,
                title: "Invitation is not pending",
                detail: "Only invitations in Pending state can be revoked.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
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
