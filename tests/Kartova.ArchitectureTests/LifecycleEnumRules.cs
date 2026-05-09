using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins the Lifecycle enum's persisted-value stability. The numeric values
/// (1=Active, 2=Deprecated, 3=Decommissioned) are stored in the
/// <c>lifecycle smallint</c> column on <c>catalog.applications</c> — reordering
/// or renumbering would silently corrupt rows already on disk. Linear ordering
/// is also pinned so future SQL filters (<c>WHERE lifecycle &gt;= :x</c>) can
/// rely on monotonic numeric values.
/// </summary>
[ExcludeFromCodeCoverage]
[TestClass]
public class LifecycleEnumRules
{
    // MSTEST0032 (assertion always true) is a false positive here: the analyzer correctly
    // sees these as compile-time-constant comparisons, but that is precisely the point of
    // a pinning test — if a future edit renumbers the enum, the constant changes and the
    // assertion flips to "always false". The runtime assertion guards the on-disk schema
    // contract documented in the class summary.
#pragma warning disable MSTEST0032
    [TestMethod]
    public void Lifecycle_has_exactly_three_members_with_explicit_values()
    {
        Assert.AreEqual(3, Enum.GetValues<Lifecycle>().Length);

        Assert.AreEqual(1, (int)Lifecycle.Active);
        Assert.AreEqual(2, (int)Lifecycle.Deprecated);
        Assert.AreEqual(3, (int)Lifecycle.Decommissioned);
    }

    [TestMethod]
    public void Lifecycle_members_are_linearly_ordered()
    {
        Assert.IsTrue((int)Lifecycle.Active < (int)Lifecycle.Deprecated);
        Assert.IsTrue((int)Lifecycle.Deprecated < (int)Lifecycle.Decommissioned);
    }
#pragma warning restore MSTEST0032
}
