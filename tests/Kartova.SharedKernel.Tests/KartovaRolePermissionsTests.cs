using Kartova.SharedKernel.Multitenancy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public sealed class KartovaRolePermissionsTests
{
    [TestMethod]
    public void Viewer_can_read_catalog_only()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);
        Assert.AreEqual(1, perms.Count);
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogRead));
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
    public void TeamAdmin_set_equals_Member_set_in_slice_7()
    {
        var member = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        var teamAdmin = KartovaRolePermissions.ForRole(KartovaRoles.TeamAdmin);
        CollectionAssert.AreEquivalent(member.ToList(), teamAdmin.ToList(),
            "TeamAdmin is forward-compat in slice 7; should match Member.");
    }

    [TestMethod]
    public void OrgAdmin_uniquely_owns_reverse_lifecycle()
    {
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse));

        foreach (var role in new[] { KartovaRoles.Viewer, KartovaRoles.Member, KartovaRoles.TeamAdmin })
        {
            var perms = KartovaRolePermissions.ForRole(role);
            Assert.IsFalse(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse),
                $"{role} must not have reverse-lifecycle permission.");
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
}
