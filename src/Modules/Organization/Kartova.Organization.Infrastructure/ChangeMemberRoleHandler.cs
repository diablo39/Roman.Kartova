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

        if (user.RealmRole == KartovaRoles.OrgAdmin && cmd.Role != KartovaRoles.OrgAdmin)
        {
            var orgAdminCount = await db.Users.CountAsync(u => u.RealmRole == KartovaRoles.OrgAdmin, ct);
            if (orgAdminCount <= 1) return ChangeMemberRoleResult.LastOrgAdminResult;
        }

        await keycloak.ChangeRealmRoleAsync(cmd.UserId, cmd.Role, ct);   // source of truth
        user.RealmRole = cmd.Role;                                        // write-through cache
        await db.SaveChangesAsync(ct);
        return ChangeMemberRoleResult.Success;
    }
}
