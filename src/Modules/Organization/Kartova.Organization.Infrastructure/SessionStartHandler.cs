using System.Security.Claims;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
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
/// realm role + team memberships. The SPA's <c>OidcCallbackHandler</c> always
/// calls this endpoint first after the KC roundtrip (Volue Identity enforces a
/// static <c>redirect_uri</c>, so it is the guaranteed first authenticated code
/// path on the client) — non-SPA clients (CLI, agent) require pre-registered
/// accounts which already went through this endpoint via the SPA. That makes
/// it safe to perform the JWT-claim users-projection upsert and the Pending →
/// Accepted invitation flip inline here rather than via a per-request
/// pipeline hook.
/// </summary>
public sealed class SessionStartHandler
{
    private readonly OrganizationDbContext _db;
    private readonly IUserDirectory _directory;
    private readonly OrgProfileQueries _orgQueries;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly UserProjectionUpdater _projection;
    private readonly TimeProvider _clock;

    public SessionStartHandler(
        OrganizationDbContext db,
        IUserDirectory directory,
        OrgProfileQueries orgQueries,
        ICurrentUser currentUser,
        ITenantContext tenant,
        UserProjectionUpdater projection,
        TimeProvider clock)
    {
        _db = db;
        _directory = directory;
        _orgQueries = orgQueries;
        _currentUser = currentUser;
        _tenant = tenant;
        _projection = projection;
        _clock = clock;
    }

    public async Task<SessionStartResponse> HandleAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // 1. Upsert users projection from JWT claims. Claim names follow OpenID
        //    Connect Core §5.1 ("sub", "email", "given_name", "family_name") —
        //    the wire contract from KC's ID/access tokens. We check both the raw
        //    OIDC form AND the .NET ClaimTypes.* form because some pipeline
        //    stages (notably DefaultClaimsTransformation) rewrite "email" to
        //    ClaimTypes.Email. UserId comes from ICurrentUser which reads "sub".
        var userId = _currentUser.UserId;
        var email = principal.FindFirst("email")?.Value
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value
                 ?? "";
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException(
                "Session bootstrap requires an 'email' claim on the JWT.");
        var given = principal.FindFirst("given_name")?.Value;
        var family = principal.FindFirst("family_name")?.Value;
        await _projection.UpsertAsync(_db, userId, email, given, family, _tenant.Id, ct);

        // 2. Invitation auto-accept: Pending → Accepted if a Pending invitation
        //    exists for this KC user AND is not expired. The expiry guard
        //    matches the prior post-auth-hook semantics — the background
        //    expirer handles stale rows; this handler only flips fresh ones.
        Invitation? acceptedInvitation = null;
        var pending = await _db.Invitations
            .FirstOrDefaultAsync(i => i.KeycloakUserId == userId && i.Status == InvitationStatus.Pending, ct);
        if (pending is not null && pending.ExpiresAt > _clock.GetUtcNow())
        {
            pending.MarkAccepted(_clock);
            await _db.SaveChangesAsync(ct);
            acceptedInvitation = pending;
        }

        // 3. Now that the users row is guaranteed fresh (step 1 wrote it), the
        //    directory lookup must succeed. A miss here is an internal
        //    invariant violation, not a degraded session.
        var me = await _directory.GetAsync(userId, ct)
                 ?? throw new InvalidOperationException(
                     "UserProjectionUpdater did not persist a users row — internal invariant violation.");

        var org = await _orgQueries.GetMyOrgAsync(ct)
                  ?? throw new InvalidOperationException(
                      "Org row missing for tenant. RLS should guarantee at-most-one visible row " +
                      "in a tenant-scoped request — a missing row indicates the tenant has no " +
                      "Organization aggregate seeded (broken provisioning), not a normal 404.");

        // Spec §3 Decision #2: each user holds exactly one realm role. Use
        // ITenantContext.Roles (populated from JWT) rather than ICurrentUser.
        // Fall back to Viewer to mirror the permission-set fallback below: a
        // tenant-scoped principal without an explicit realm role gets the
        // read-only baseline rather than a 500.
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

        // 4. AcceptedInvitation block — populated only if we just flipped one in
        //    step 2. Defensive null checks (inviter directory miss, missing
        //    AcceptedAt) collapse the welcome block to null rather than emit a
        //    partial response.
        AcceptedInvitationInfo? accepted = null;
        if (acceptedInvitation is not null)
        {
            var invitedBy = await _directory.GetAsync(acceptedInvitation.InvitedByUserId, ct);
            if (invitedBy is not null && acceptedInvitation.AcceptedAt is { } acceptedAt)
            {
                accepted = new AcceptedInvitationInfo(
                    OrgDisplayName: org.DisplayName,
                    InvitedBy: invitedBy,
                    InvitedAt: acceptedInvitation.InvitedAt,
                    AcceptedAt: acceptedAt);
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
