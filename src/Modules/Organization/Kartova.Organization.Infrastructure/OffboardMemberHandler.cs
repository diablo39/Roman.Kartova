using Kartova.Organization.Application;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Offboards a member (slice-10 Task 6, spec §6.7): deletes the KeyCloak identity,
/// cascade-removes the member's team memberships, and deletes the local <c>users</c>
/// projection row. Application.CreatedByUserId is immutable history — no ownership
/// reassignment occurs (ADR-0102 update).
///
/// <para>
/// Guard order is deliberate (NotFound → Self → LastOrgAdmin): the self-check runs
/// before the last-admin guard so an OrgAdmin trying to remove themselves sees the
/// precise "cannot offboard self" 409. All guards return before any side-effect, so
/// neither KeyCloak nor the DB is touched on a rejected request.
/// </para>
///
/// <para>
/// Transactions (ADR-0090): the handler never opens or commits a transaction. This
/// handler's OrganizationDbContext save flushes within the per-request
/// <c>ITenantScope</c> transaction; the endpoint filter commits at request end. If
/// <see cref="IKeycloakAdminClient.DeleteUserAsync"/> throws, the uncaught
/// <see cref="KeycloakAdminException"/> aborts the request and the ambient transaction
/// rolls the DB changes back — so a KC outage cannot leave the projection in a
/// partially-offboarded state. The KC delete is intentionally NOT caught here; the
/// global <c>KeycloakAdminExceptionHandler</c> maps it to a typed 502 (Part D).
/// </para>
/// </summary>
public sealed class OffboardMemberHandler(IKeycloakAdminClient keycloak)
{
    public async Task<OffboardMemberResult> Handle(
        OffboardMemberCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (target is null) return OffboardMemberResult.NotFoundResult;
        if (cmd.UserId == cmd.ActingUserId) return OffboardMemberResult.SelfResult;

        if (target.RealmRole == KartovaRoles.OrgAdmin)
        {
            var orgAdminCount = await db.Users.CountAsync(u => u.RealmRole == KartovaRoles.OrgAdmin, ct);
            if (orgAdminCount <= 1) return OffboardMemberResult.LastOrgAdminResult;
        }

        await keycloak.DeleteUserAsync(cmd.UserId, ct);

        var memberships = await db.TeamMembers.Where(m => m.UserId == cmd.UserId).ToListAsync(ct);
        db.TeamMembers.RemoveRange(memberships);
        db.Users.Remove(target);
        await db.SaveChangesAsync(ct);

        return OffboardMemberResult.Success;
    }
}
