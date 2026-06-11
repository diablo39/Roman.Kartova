using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Single definition of the "an organisation must always retain at least one OrgAdmin"
/// invariant, shared by <see cref="ChangeMemberRoleHandler"/> (demotion) and
/// <see cref="OffboardMemberHandler"/> (removal). Keeping the rule (and its <c>&lt;= 1</c>
/// boundary) in one place means a future refinement — e.g. counting only active admins —
/// propagates to both call sites. RLS auto-scopes the count to the current tenant (ADR-0090).
/// </summary>
internal static class OrgAdminFloor
{
    /// <summary>
    /// True when <paramref name="target"/> is an OrgAdmin and is the only one left in the
    /// tenant — i.e. removing or demoting them would drop the org to zero OrgAdmins. Returns
    /// false (without querying) when the target is not an OrgAdmin.
    /// </summary>
    public static async Task<bool> IsLastOrgAdminAsync(
        OrganizationDbContext db, User target, CancellationToken ct) =>
        target.RealmRole == KartovaRoles.OrgAdmin
        && await db.Users.CountAsync(u => u.RealmRole == KartovaRoles.OrgAdmin, ct) <= 1;
}
