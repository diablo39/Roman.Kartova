using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Kartova.Catalog.Domain;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins the Lifecycle enum's wire stability. The numeric values (1=Active,
/// 2=Deprecated, 3=Decommissioned) are load-bearing — comparison ops in
/// Application.Decommission rely on monotonic ordering. Inserting or
/// reordering members shifts every comparison and changes the wire shape.
/// These tests force a deliberate reckoning when changing the enum.
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
