using System.Security.Claims;
using Kartova.Organization.Application;
using Microsoft.AspNetCore.Http;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Endpoint delegates for the auth bootstrap surface (slice 9 spec §6.7 /
/// §9.8) under <c>/api/v1/auth</c>. Lives in the Organization module because
/// the handler owns the Invitation read and the org-profile join — the URL
/// diverges from <c>/api/v1/organizations</c> only because "session" isn't an
/// organization resource (it's a per-request bootstrap). Split out of the
/// legacy <c>OrganizationEndpointDelegates</c> aggregator (slice-9 carry-
/// forward #16) — behavior is identical, only the host type name changed.
/// </summary>
internal static class AuthEndpointDelegates
{
    /// <summary>
    /// <c>POST /api/v1/auth/session</c>: single-shot post-login payload. Returns
    /// caller identity + role + permission set + team memberships + org profile
    /// + (optionally) the just-accepted-invitation block used to drive the
    /// SPA's one-time welcome screen. Lives inside the Organization module
    /// because the handler owns the Invitation read and the org-profile join —
    /// the URL diverges from <c>/api/v1/organizations</c> only because "session"
    /// isn't an organization resource (it's a per-request bootstrap).
    /// <para>
    /// <see cref="ClaimsPrincipal"/> is bound automatically by Minimal API from
    /// <c>HttpContext.User</c>; threading it explicitly through the delegate
    /// avoids depending on <see cref="IHttpContextAccessor"/> inside the
    /// handler and keeps the JWT claim reads testable.
    /// </para>
    /// </summary>
    internal static async Task<IResult> StartSessionAsync(
        SessionStartHandler handler,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var response = await handler.HandleAsync(principal, ct);
        return Results.Ok(response);
    }
}
