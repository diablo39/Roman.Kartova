using Kartova.Organization.Application;
using Kartova.SharedKernel.Identity;
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
/// <c>ITenantScope</c> transaction; the endpoint filter commits at request end. A
/// <see cref="KeycloakAdminError.NotFound"/> from
/// <see cref="IKeycloakAdminClient.DeleteUserAsync"/> is treated as success-equivalent (the
/// identity is already gone — idempotent), so the local projection + memberships are still
/// removed; this also reconciles a previously orphaned projection row. Any other
/// <see cref="KeycloakAdminException"/> is left to propagate: the ambient transaction rolls
/// the DB changes back — so a KC outage cannot leave the projection partially-offboarded —
/// and the global <c>KeycloakAdminExceptionHandler</c> maps it to a typed 502 (Part D).
/// </para>
/// </summary>
public sealed class OffboardMemberHandler(IKeycloakAdminClient keycloak)
{
    public async Task<OffboardMemberResult> Handle(
        OffboardMemberCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.Target.Value, ct);
        if (target is null) return OffboardMemberResult.NotFoundResult;
        if (cmd.Target.Value == cmd.Actor.Value) return OffboardMemberResult.SelfResult;

        if (await OrgAdminFloor.IsLastOrgAdminAsync(db, target, ct))
            return OffboardMemberResult.LastOrgAdminResult;

        try
        {
            await keycloak.DeleteUserAsync(cmd.Target.Value, ct);
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
        {
            // Idempotent: the KC identity is already gone (drift, or a prior partial
            // offboard). The desired end state already matches, so fall through and complete
            // the local cleanup rather than masking a permanent 404 as a transient 502.
        }

        var memberships = await db.TeamMembers.Where(m => m.UserId == cmd.Target.Value).ToListAsync(ct);
        db.TeamMembers.RemoveRange(memberships);
        db.Users.Remove(target);
        await db.SaveChangesAsync(ct);

        return OffboardMemberResult.Success;
    }
}
