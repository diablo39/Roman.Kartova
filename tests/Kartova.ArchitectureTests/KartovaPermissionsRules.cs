using System.IO;
using System.Reflection;
using System.Text.Json;
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

    [TestMethod]
    public void Ts_snapshot_equals_csharp_KartovaPermissions_All()
    {
        var snapshotPath = FindRepoFile("web/src/shared/auth/permissions.snapshot.json");
        Assert.IsTrue(File.Exists(snapshotPath),
            $"Drift sentinel missing: {snapshotPath}. The TS side must commit a JSON list of permission names.");

        using var doc = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        var snapshot = doc.RootElement.EnumerateArray()
                                      .Select(e => e.GetString()!)
                                      .ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            KartovaPermissions.All.ToList(),
            snapshot.ToList(),
            "TS permissions.snapshot.json must match C# KartovaPermissions.All exactly.");
    }

    [TestMethod]
    public void Team_permissions_are_present_in_KartovaPermissions_All()
    {
        string[] expected = ["team.read", "team.create", "team.metadata.edit", "team.delete", "team.members.manage"];
        foreach (var perm in expected)
            Assert.IsTrue(KartovaPermissions.All.Contains(perm), $"missing: {perm}");
    }

    [TestMethod]
    [DataRow(KartovaRoles.Viewer,   new[] { "team.read" })]
    [DataRow(KartovaRoles.Member,   new[] { "team.read" })]
    [DataRow(KartovaRoles.TeamAdmin, new[] { "team.read", "team.metadata.edit", "team.delete", "team.members.manage" })]
    [DataRow(KartovaRoles.OrgAdmin, new[] { "team.read", "team.create", "team.metadata.edit", "team.delete", "team.members.manage" })]
    public void Role_permissions_include_team_perms(string role, string[] requiredPerms)
    {
        Assert.IsTrue(KartovaRolePermissions.Map.TryGetValue(role, out var perms), $"role missing: {role}");
        foreach (var p in requiredPerms)
            Assert.IsTrue(perms.Contains(p), $"role {role} missing perm {p}");
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Kartova.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Kartova.slnx not found walking up from current directory.");
        return Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
