using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Endpoint delegates for the Invitation surface (slice 9 spec §6.7) under
/// <c>/api/v1/organizations/invitations</c>: cursor-paginated list, create
/// (with the three-way 409 conflict matrix), and revoke. Split out of the
/// legacy <c>OrganizationEndpointDelegates</c> aggregator (slice-9 carry-
/// forward #16) — behavior is identical, only the host type name changed.
/// </summary>
internal static class InvitationEndpointDelegates
{
    /// <summary>
    /// <c>GET /invitations</c>: cursor-paginated list of invitations visible
    /// to the current tenant (RLS-filtered). Optional <c>status</c> filter
    /// narrows to a single <see cref="InvitationStatus"/>. Parsing mirrors
    /// <see cref="TeamEndpointDelegates.ListTeamsAsync"/> — same paging exceptions translate to
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
            RevokeResult.Upstream => Results.Problem(
                type: ProblemTypes.ServiceUnavailable,
                title: "Upstream KeyCloak error",
                detail: "The identity provider rejected the user deletion. The invitation remains Pending; please retry shortly.",
                statusCode: StatusCodes.Status502BadGateway),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
