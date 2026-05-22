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
}
