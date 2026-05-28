using Kartova.Organization.Contracts;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Builds the post-login payload for <c>POST /api/v1/auth/session</c> (slice 9
/// spec §6.7 / §9.8). Lives in Infrastructure (not Application) because the
/// handler depends on <see cref="OrganizationDbContext"/> for the Invitation
/// lookup — same placement rationale as <see cref="OrgProfileQueries"/>.
///
/// Pipeline contract: the endpoint is mapped behind <c>RequireTenantScope</c>,
/// so by the time the handler runs the <see cref="ITenantScope"/> has been
/// opened and <see cref="ITenantContext"/> has been populated with the JWT's
/// realm role + team memberships. <see cref="ICurrentUser.JustAcceptedInvitationId"/>
/// is set on the <em>same request</em> in which the post-auth sync hook flipped
/// a Pending invitation to Accepted (spec §5.2) — so the welcome payload is a
/// deterministic single-request signal, never recovered from later requests.
/// </summary>
public sealed class SessionStartHandler
{
    private readonly OrganizationDbContext _db;
    private readonly IUserDirectory _directory;
    private readonly OrgProfileQueries _orgQueries;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;

    public SessionStartHandler(
        OrganizationDbContext db,
        IUserDirectory directory,
        OrgProfileQueries orgQueries,
        ICurrentUser currentUser,
        ITenantContext tenant)
    {
        _db = db;
        _directory = directory;
        _orgQueries = orgQueries;
        _currentUser = currentUser;
        _tenant = tenant;
    }

    public async Task<SessionStartResponse> HandleAsync(CancellationToken ct)
    {
        // The local users projection is fresh by the time we reach the handler —
        // TenantClaimsTransformation runs the post-auth hook BEFORE the endpoint
        // executes (spec §9.1 + §9.8). If the directory miss returns null (e.g.
        // first request before the hook ran in a degraded mode), fall back to a
        // synthesized placeholder so the SPA still gets a parseable Me block
        // instead of a 500.
        var me = await _directory.GetAsync(_currentUser.UserId, ct)
                 ?? new UserDisplayInfo(_currentUser.UserId, _currentUser.UserId.ToString(), "");

        var org = await _orgQueries.GetMyOrgAsync(ct)
                  ?? throw new InvalidOperationException(
                      "Org row missing for tenant. RLS should guarantee at-most-one visible row " +
                      "in a tenant-scoped request — a missing row indicates the tenant has no " +
                      "Organization aggregate seeded (broken provisioning), not a normal 404.");

        // Spec §3 Decision #2: each user holds exactly one realm role. Use
        // ITenantContext.Roles (populated from JWT) rather than ICurrentUser —
        // ICurrentUser exposes only UserId / TeamMemberships / TeamIds /
        // JustAcceptedInvitationId. Fall back to Viewer to mirror the
        // permission-set fallback below: a tenant-scoped principal without an
        // explicit realm role gets the read-only baseline rather than a 500.
        var role = _tenant.Roles.FirstOrDefault() ?? KartovaRoles.Viewer;

        // Use ForRole (not Map[role]) — Map only contains entries for the four
        // tenant-scoped roles (Viewer/Member/TeamAdmin/OrgAdmin). PlatformAdmin
        // and ServiceAccount are deliberately absent because they're orthogonal
        // to tenants; if such a principal ever reaches this endpoint, ForRole
        // returns an empty set instead of throwing KeyNotFoundException.
        var permissions = KartovaRolePermissions.ForRole(role).ToArray();

        // TeamMembershipInfo.Role is TeamRoleKind ("Admin" / "Member"). The wire
        // shape MeTeamMembership.Role is a string — ToString() matches the same
        // convention used by GetMePermissions and TeamMemberResponse.
        var teams = _currentUser.TeamMemberships
            .Select(t => new MeTeamMembership(t.TeamId, t.Role.ToString()))
            .ToArray();

        AcceptedInvitationInfo? accepted = null;
        if (_currentUser.JustAcceptedInvitationId is { } invitationId)
        {
            // RLS scopes the lookup to the current tenant automatically. The
            // backing-field selector mirrors RevokeInvitationHandler so the
            // _id field name lives in exactly one place (InvitationSortSpecs).
            var inv = await _db.Invitations
                .AsNoTracking()
                .FirstOrDefaultAsync(InvitationSortSpecs.IdEquals(invitationId), ct);

            if (inv is not null)
            {
                var invitedBy = await _directory.GetAsync(inv.InvitedByUserId, ct);
                // AcceptedAt is set by Invitation.MarkAccepted, which the
                // post-auth hook called in the same request before the handler
                // runs (spec §5.2). The defensive null check stops a partial
                // upgrade path from emitting an AcceptedInvitation block with a
                // sentinel timestamp; in that edge case the SPA simply doesn't
                // see a welcome banner — equivalent to a normal re-login.
                if (invitedBy is not null && inv.AcceptedAt is { } acceptedAt)
                {
                    accepted = new AcceptedInvitationInfo(
                        OrgDisplayName: org.DisplayName,
                        InvitedBy: invitedBy,
                        InvitedAt: inv.InvitedAt,
                        AcceptedAt: acceptedAt);
                }
            }
        }

        return new SessionStartResponse(
            Me: me,
            Role: role,
            Permissions: permissions,
            Teams: teams,
            Organization: org,
            AcceptedInvitation: accepted);
    }
}
