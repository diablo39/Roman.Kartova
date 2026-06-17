using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class ChangeMemberRoleHandler(IKeycloakAdminClient keycloak, IAuditWriter audit)
{
    public async Task<ChangeMemberRoleOutcome> Handle(
        ChangeMemberRoleCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        if (!KartovaRoles.All.Contains(cmd.Role))
            return ChangeMemberRoleOutcome.InvalidRole;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (user is null) return ChangeMemberRoleOutcome.NotFound;

        if (cmd.Role != KartovaRoles.OrgAdmin && await OrgAdminFloor.IsLastOrgAdminAsync(db, user, ct))
            return ChangeMemberRoleOutcome.LastOrgAdmin;

        var oldRole = user.RealmRole;                                     // snapshot before write-through
        await keycloak.ChangeRealmRoleAsync(cmd.UserId, cmd.Role, ct);    // source of truth
        user.RealmRole = cmd.Role;                                        // write-through cache
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.MemberRoleChanged,
            AuditTargetTypes.User,
            cmd.UserId.ToString(),
            new Dictionary<string, string?> { ["oldRole"] = oldRole, ["newRole"] = cmd.Role }), ct);

        return ChangeMemberRoleOutcome.Success;
    }
}
