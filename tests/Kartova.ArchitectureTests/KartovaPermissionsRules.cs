using System.Reflection;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.ArchitectureTests;

[TestClass]
public sealed class KartovaPermissionsRules
{
    [TestMethod]
    public void All_collection_contains_every_public_string_constant()
    {
        var declared = typeof(KartovaPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var v in declared)
        {
            Assert.IsTrue(KartovaPermissions.All.Contains(v),
                $"KartovaPermissions.All must list every declared constant — missing '{v}'.");
        }

        Assert.AreEqual(declared.Count, KartovaPermissions.All.Count,
            "KartovaPermissions.All must not contain entries that are not declared as constants.");
    }

    [TestMethod]
    public void Every_permission_appears_in_at_least_one_role_set()
    {
        var permissionsInUse = KartovaRolePermissions.Map
            .SelectMany(kvp => kvp.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var perm in KartovaPermissions.All)
        {
            Assert.IsTrue(permissionsInUse.Contains(perm),
                $"Orphan permission '{perm}' — not granted to any role in KartovaRolePermissions.Map.");
        }
    }

    [TestMethod]
    public void Every_mapped_value_is_a_known_permission()
    {
        var declared = new HashSet<string>(KartovaPermissions.All, StringComparer.Ordinal);

        foreach (var (role, perms) in KartovaRolePermissions.Map)
        {
            foreach (var perm in perms)
            {
                Assert.IsTrue(declared.Contains(perm),
                    $"Role {role} grants unknown permission '{perm}' — not declared in KartovaPermissions.");
            }
        }
    }

    [TestMethod]
    public void Every_map_key_is_a_known_role()
    {
        var declaredRoles = typeof(KartovaRoles)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var role in KartovaRolePermissions.Map.Keys)
        {
            Assert.IsTrue(declaredRoles.Contains(role),
                $"Map key '{role}' is not declared in KartovaRoles.");
        }
    }
}
