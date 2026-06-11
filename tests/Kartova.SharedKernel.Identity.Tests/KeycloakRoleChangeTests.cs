using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Identity.Tests;

/// <summary>
/// Unit tests for <see cref="KeycloakRoleChange.RolesToRemove"/>.
/// Tests the filter-logic predicate in isolation (no HTTP / TokenClient needed).
/// The HTTP-level assertions (DELETE issued, POST issued) are covered by the
/// Task 5 integration test against a real KeyCloak container.
/// </summary>
[TestClass]
public sealed class KeycloakRoleChangeTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (string Id, string Name) R(string id, string name) => (id, name);

    // ── core predicate ───────────────────────────────────────────────────────

    [TestMethod]
    public void RolesToRemove_removes_other_business_roles_and_keeps_newRole()
    {
        // current: Member (business) + some-other (non-business)
        // target:  OrgAdmin
        // expected to remove: Member only
        var current = new[] { R("id-member", KartovaRoles.Member), R("id-other", "some-other-role") };

        var result = KeycloakRoleChange.RolesToRemove(current, KartovaRoles.OrgAdmin).ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(KartovaRoles.Member, result[0].Name);
    }

    [TestMethod]
    public void RolesToRemove_does_not_include_the_target_role_when_already_assigned()
    {
        // Edge case: user already has OrgAdmin; we're re-assigning OrgAdmin.
        // toRemove should be empty so we skip the DELETE call.
        var current = new[] { R("id-orgadmin", KartovaRoles.OrgAdmin) };

        var result = KeycloakRoleChange.RolesToRemove(current, KartovaRoles.OrgAdmin).ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void RolesToRemove_removes_all_three_business_roles_when_none_matches_target()
    {
        // Pathological: user somehow holds all three business roles.
        var current = new[]
        {
            R("id-viewer",   KartovaRoles.Viewer),
            R("id-member",   KartovaRoles.Member),
            R("id-orgadmin", KartovaRoles.OrgAdmin),
        };

        var result = KeycloakRoleChange.RolesToRemove(current, "NonExistent").ToList();

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEquivalent(
            new[] { KartovaRoles.Viewer, KartovaRoles.Member, KartovaRoles.OrgAdmin },
            result.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void RolesToRemove_ignores_platform_admin_role()
    {
        // PlatformAdmin is NOT in KartovaRoles.All — must never be touched.
        var current = new[]
        {
            R("id-platform", KartovaRoles.PlatformAdmin),
            R("id-member",   KartovaRoles.Member),
        };

        var result = KeycloakRoleChange.RolesToRemove(current, KartovaRoles.OrgAdmin).ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(KartovaRoles.Member, result[0].Name);
        Assert.IsFalse(result.Any(r => r.Name == KartovaRoles.PlatformAdmin),
            "PlatformAdmin must never appear in the remove list.");
    }

    [TestMethod]
    public void RolesToRemove_returns_empty_when_no_business_roles_assigned()
    {
        var current = new[] { R("id-other", "some-other-role") };

        var result = KeycloakRoleChange.RolesToRemove(current, KartovaRoles.Viewer).ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void RolesToRemove_returns_empty_for_empty_input()
    {
        var result = KeycloakRoleChange.RolesToRemove([], KartovaRoles.Member).ToList();

        Assert.AreEqual(0, result.Count);
    }

    // ── interface contract ───────────────────────────────────────────────────

    [TestMethod]
    public void IKeycloakAdminClient_exposes_ChangeRealmRoleAsync()
    {
        // Guard: confirms the method is on the interface and has the expected signature.
        var method = typeof(IKeycloakAdminClient).GetMethod(
            nameof(IKeycloakAdminClient.ChangeRealmRoleAsync),
            [typeof(Guid), typeof(string), typeof(CancellationToken)]);

        Assert.IsNotNull(method, "IKeycloakAdminClient.ChangeRealmRoleAsync must exist with (Guid, string, CancellationToken) signature.");
        Assert.AreEqual(typeof(System.Threading.Tasks.Task), method.ReturnType);
    }
}
