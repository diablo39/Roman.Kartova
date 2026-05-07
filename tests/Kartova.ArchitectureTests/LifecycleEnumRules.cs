using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
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
public class LifecycleEnumRules
{
    [Fact]
    public void Lifecycle_has_exactly_three_members_with_explicit_values()
    {
        Enum.GetValues<Lifecycle>().Should().HaveCount(3);

        ((int)Lifecycle.Active).Should().Be(1);
        ((int)Lifecycle.Deprecated).Should().Be(2);
        ((int)Lifecycle.Decommissioned).Should().Be(3);
    }

    [Fact]
    public void Lifecycle_members_are_linearly_ordered()
    {
        ((int)Lifecycle.Active).Should().BeLessThan((int)Lifecycle.Deprecated);
        ((int)Lifecycle.Deprecated).Should().BeLessThan((int)Lifecycle.Decommissioned);
    }
}
