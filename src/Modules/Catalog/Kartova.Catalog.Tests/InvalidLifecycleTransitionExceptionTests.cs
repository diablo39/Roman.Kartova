using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Tests;

public class InvalidLifecycleTransitionExceptionTests
{
    [Fact]
    public void Message_for_null_reason_omits_parenthetical()
    {
        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Decommissioned,
            "Deprecate",
            sunsetDate: null,
            reason: null);

        ex.Message.Should().Be("Cannot deprecate application currently in state Decommissioned.");
    }

    [Fact]
    public void Message_for_non_null_reason_includes_parenthetical()
    {
        var sunsetDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Deprecated,
            "Decommission",
            sunsetDate: sunsetDate,
            reason: "before-sunset-date");

        ex.Message.Should().Be("Cannot decommission application currently in state Deprecated (before-sunset-date).");
    }

    [Fact]
    public void Properties_match_constructor_args()
    {
        var sunsetDate = new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero);

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Active,
            "Decommission",
            sunsetDate: sunsetDate,
            reason: "before-sunset-date");

        ex.CurrentLifecycle.Should().Be(Lifecycle.Active);
        ex.AttemptedTransition.Should().Be("Decommission");
        ex.SunsetDate.Should().Be(sunsetDate);
        ex.Reason.Should().Be("before-sunset-date");
    }

    [Theory]
    [InlineData(Lifecycle.Active, "active")]
    [InlineData(Lifecycle.Deprecated, "deprecated")]
    [InlineData(Lifecycle.Decommissioned, "decommissioned")]
    public void ILifecycleConflict_CurrentLifecycleName_returns_camelCase_wire_shape(
        Lifecycle current, string expectedWire)
    {
        // Pins the explicit-interface implementation that lower-cases the enum
        // name to match JsonStringEnumConverter(JsonNamingPolicy.CamelCase) on
        // ApplicationResponse.lifecycle (ADR-0095). If a refactor accidentally
        // dropped the `: ILifecycleConflict` declaration or the explicit member
        // (e.g. let the public `CurrentLifecycle` enum property satisfy the
        // contract via auto-conversion), the upcast here would call a different
        // implementation that returns PascalCase — caught by this assertion.
        var ex = new InvalidLifecycleTransitionException(current, "Deprecate");

        ((ILifecycleConflict)ex).CurrentLifecycleName.Should().Be(expectedWire);
    }
}
