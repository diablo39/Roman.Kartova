using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class ChangeMemberRoleHandler(IKeycloakAdminClient keycloak)
{
    public async Task<ChangeMemberRoleResult> Handle(
        ChangeMemberRoleCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        if (!KartovaRoles.All.Contains(cmd.Role))
            return ChangeMemberRoleResult.InvalidRoleResult;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (user is null) return ChangeMemberRoleResult.NotFoundResult;

        // Demotion only: promoting *to* OrgAdmin can never breach the floor, so skip the
        // count query in that case (short-circuit). The OrgAdmin-floor invariant itself
        // lives in OrgAdminFloor, shared with OffboardMemberHandler.
        if (cmd.Role != KartovaRoles.OrgAdmin && await OrgAdminFloor.IsLastOrgAdminAsync(db, user, ct))
            return ChangeMemberRoleResult.LastOrgAdminResult;

        await keycloak.ChangeRealmRoleAsync(cmd.UserId, cmd.Role, ct);   // source of truth
        user.RealmRole = cmd.Role;                                        // write-through cache
        await db.SaveChangesAsync(ct);
        return ChangeMemberRoleResult.Success;
    }
}
