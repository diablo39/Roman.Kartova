using Kartova.SharedKernel.Multitenancy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public sealed class KartovaRolePermissionsTests
{
    [TestMethod]
    public void Viewer_can_read_catalog_and_teams()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogRead));
        Assert.IsTrue(perms.Contains(KartovaPermissions.TeamRead));
        Assert.IsFalse(perms.Contains(KartovaPermissions.CatalogApplicationsRegister),
            "Viewer must not have write permissions on catalog.");
    }

    [TestMethod]
    public void Member_can_read_register_edit_forward_lifecycle()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogRead));
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogApplicationsRegister));
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogApplicationsEditMetadata));
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleForward));
        Assert.IsFalse(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse));
    }

    [TestMethod]
    public void OrgAdmin_does_not_carry_team_mutation_claims_resource_gate_owns_them()
    {
        // ADR-0101: team metadata/delete/members are no longer permission claims —
        // team-admin authority is the per-team Admin membership via TeamAdminOfThis.
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.TeamRead));
        Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.TeamCreate));
        Assert.IsFalse(orgAdmin.Contains("team.metadata.edit"));
        Assert.IsFalse(orgAdmin.Contains("team.delete"));
        Assert.IsFalse(orgAdmin.Contains("team.members.manage"));
    }

    [TestMethod]
    public void OrgAdmin_uniquely_owns_reverse_lifecycle()
    {
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse));

        foreach (var role in new[] { KartovaRoles.Viewer, KartovaRoles.Member })
        {
            var perms = KartovaRolePermissions.ForRole(role);
            Assert.IsFalse(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse),
                $"{role} must not have reverse-lifecycle permission.");
        }
    }

    [TestMethod]
    public void OrgAdmin_uniquely_owns_override_lifecycle()
    {
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.CatalogApplicationsLifecycleOverride));

        foreach (var role in new[] { KartovaRoles.Viewer, KartovaRoles.Member })
        {
            var perms = KartovaRolePermissions.ForRole(role);
            Assert.IsFalse(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleOverride),
                $"{role} must not have override-lifecycle permission.");
        }
    }

    [TestMethod]
    public void Viewer_subset_of_Member()
    {
        var viewer = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);
        var member = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        Assert.IsTrue(viewer.IsSubsetOf(member));
    }

    [TestMethod]
    public void Member_subset_of_OrgAdmin()
    {
        var member = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(member.IsSubsetOf(orgAdmin));
    }

    [TestMethod]
    public void PlatformAdmin_has_no_catalog_permissions()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.PlatformAdmin);
        Assert.AreEqual(0, perms.Count);
    }

    [TestMethod]
    public void ServiceAccount_has_no_realm_role_yet_returns_empty_set()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.ServiceAccount);
        Assert.AreEqual(0, perms.Count);
    }

    [TestMethod]
    public void Unknown_role_returns_empty_set()
    {
        var perms = KartovaRolePermissions.ForRole("not-a-real-role");
        Assert.AreEqual(0, perms.Count);
    }

    [TestMethod]
    public void OrgAdmin_has_user_management_permissions()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(perms.Contains(KartovaPermissions.OrgUsersRoleChange));
        Assert.IsTrue(perms.Contains(KartovaPermissions.OrgUsersRemove));
    }

    [TestMethod]
    [DataRow("Viewer")]
    [DataRow("Member")]
    public void NonAdmin_roles_lack_user_management_permissions(string role)
    {
        var perms = KartovaRolePermissions.ForRole(role);
        Assert.IsFalse(perms.Contains(KartovaPermissions.OrgUsersRoleChange));
        Assert.IsFalse(perms.Contains(KartovaPermissions.OrgUsersRemove));
    }

    [TestMethod]
    public void Member_and_OrgAdmin_can_register_services_but_Viewer_cannot()
    {
        var member = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        var viewer = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);

        Assert.IsTrue(member.Contains(KartovaPermissions.CatalogServicesRegister));
        Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.CatalogServicesRegister));
        Assert.IsFalse(viewer.Contains(KartovaPermissions.CatalogServicesRegister));
    }

    [TestMethod]
    public void CatalogServicesRegister_is_in_the_All_set() =>
        Assert.IsTrue(KartovaPermissions.All.Contains(KartovaPermissions.CatalogServicesRegister));

    [TestMethod]
    public void RelationshipsWrite_granted_to_member_and_orgadmin_not_viewer()
    {
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.Member)
            .Contains(KartovaPermissions.CatalogRelationshipsWrite));
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin)
            .Contains(KartovaPermissions.CatalogRelationshipsWrite));
        Assert.IsFalse(KartovaRolePermissions.ForRole(KartovaRoles.Viewer)
            .Contains(KartovaPermissions.CatalogRelationshipsWrite));
    }

    [TestMethod]
    public void Member_and_OrgAdmin_can_register_apis_but_Viewer_cannot()
    {
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.Member).Contains(KartovaPermissions.CatalogApisRegister));
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin).Contains(KartovaPermissions.CatalogApisRegister));
        Assert.IsFalse(KartovaRolePermissions.ForRole(KartovaRoles.Viewer).Contains(KartovaPermissions.CatalogApisRegister));
    }

    [TestMethod]
    public void CatalogApisRegister_is_in_the_All_set() =>
        Assert.IsTrue(KartovaPermissions.All.Contains(KartovaPermissions.CatalogApisRegister));
}
