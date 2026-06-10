using Kartova.Organization.Application;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Offboards a member (slice-10 Task 6, spec §6.7): reassigns the member's owned catalog
/// applications to a successor (cross-module via <see cref="IApplicationOwnerReassigner"/>),
/// deletes the KeyCloak identity, cascade-removes the member's team memberships, and deletes
/// the local <c>users</c> projection row.
///
/// <para>
/// Guard order is deliberate (NotFound → Self → InvalidSuccessor → LastOrgAdmin): the self-check
/// runs before the successor lookup so an OrgAdmin trying to remove themselves sees the precise
/// "cannot offboard self" 409 rather than a confusing "invalid successor" 422 if they also passed
/// their own id as the successor. All guards return before any side-effect, so neither KeyCloak nor
/// the reassigner is touched on a rejected request.
/// </para>
///
/// <para>
/// Transactions (ADR-0090): the handler never opens or commits a transaction. Both the reassigner's
/// CatalogDbContext save and this handler's OrganizationDbContext save flush within the same
/// per-request <c>ITenantScope</c> transaction; the endpoint filter commits at request end. If
/// <see cref="IKeycloakAdminClient.DeleteUserAsync"/> throws AFTER the reassignment flush, the
/// uncaught <see cref="KeycloakAdminException"/> aborts the request and the ambient transaction
/// rolls the reassignment back — so a KC outage cannot leave applications transferred while the
/// member's identity still exists. The KC delete is intentionally NOT caught here; the global
/// <c>KeycloakAdminExceptionHandler</c> maps it to a typed 502 (Part D).
/// </para>
/// </summary>
public sealed class OffboardMemberHandler(
    IKeycloakAdminClient keycloak, IApplicationOwnerReassigner reassigner)
{
    public async Task<OffboardMemberResult> Handle(
        OffboardMemberCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (target is null) return OffboardMemberResult.NotFoundResult;
        if (cmd.UserId == cmd.ActingUserId) return OffboardMemberResult.SelfResult;

        var successor = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.SuccessorUserId, ct);
        if (successor is null || cmd.SuccessorUserId == cmd.UserId)
            return OffboardMemberResult.InvalidSuccessorResult;

        if (target.RealmRole == KartovaRoles.OrgAdmin)
        {
            var orgAdminCount = await db.Users.CountAsync(u => u.RealmRole == KartovaRoles.OrgAdmin, ct);
            if (orgAdminCount <= 1) return OffboardMemberResult.LastOrgAdminResult;
        }

        var reassigned = await reassigner.ReassignOwnerAsync(cmd.UserId, cmd.SuccessorUserId, ct);
        await keycloak.DeleteUserAsync(cmd.UserId, ct);

        var memberships = await db.TeamMembers.Where(m => m.UserId == cmd.UserId).ToListAsync(ct);
        db.TeamMembers.RemoveRange(memberships);
        db.Users.Remove(target);
        await db.SaveChangesAsync(ct);

        return OffboardMemberResult.Success(reassigned);
    }
}
