using Kartova.SharedKernel.Multitenancy;

namespace Kartova.ArchitectureTests;

[TestClass]
public sealed class OrganizationPermissionMatrixTests
{
    private static readonly (string Role, string[] Expected)[] Matrix =
    [
        (KartovaRoles.Viewer,    [KartovaPermissions.OrgProfileRead, KartovaPermissions.OrgUsersRead]),
        (KartovaRoles.Member,    [KartovaPermissions.OrgProfileRead, KartovaPermissions.OrgUsersRead, KartovaPermissions.OrgUsersSearch]),
        (KartovaRoles.OrgAdmin,  [
            KartovaPermissions.OrgProfileRead, KartovaPermissions.OrgProfileEdit,
            KartovaPermissions.OrgInvitationsRead, KartovaPermissions.OrgInvitationsCreate, KartovaPermissions.OrgInvitationsRevoke,
            KartovaPermissions.OrgUsersRead, KartovaPermissions.OrgUsersSearch,
            KartovaPermissions.OrgUsersRoleChange, KartovaPermissions.OrgUsersRemove,
        ]),
    ];

    [TestMethod]
    public void Each_role_holds_exactly_its_expected_org_permissions()
    {
        foreach (var (role, expected) in Matrix)
        {
            var actualOrg = KartovaRolePermissions.Map[role]
                .Where(p => p.StartsWith("org.", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                expected.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
                actualOrg,
                $"Role {role} has mismatched org.* permissions.");
        }
    }
}
