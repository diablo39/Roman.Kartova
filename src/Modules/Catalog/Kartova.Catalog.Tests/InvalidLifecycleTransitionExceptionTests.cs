using Kartova.Catalog.Domain;
using Kartova.SharedKernel;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Kartova.Catalog.Tests;

[TestClass]
public class InvalidLifecycleTransitionExceptionTests
{
    [TestMethod]
    public void Message_for_null_reason_omits_parenthetical()
    {
        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Decommissioned,
            "Deprecate",
            sunsetDate: null,
            reason: null);

        Assert.AreEqual("Cannot deprecate application currently in state Decommissioned.", ex.Message);
    }

    [TestMethod]
    public void Message_for_non_null_reason_includes_parenthetical()
    {
        var sunsetDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Deprecated,
            "Decommission",
            sunsetDate: sunsetDate,
            reason: "before-sunset-date");

        Assert.AreEqual("Cannot decommission application currently in state Deprecated (before-sunset-date).", ex.Message);
    }

    [TestMethod]
    public void Properties_match_constructor_args()
    {
        var sunsetDate = new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero);

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Active,
            "Decommission",
            sunsetDate: sunsetDate,
            reason: "before-sunset-date");

        Assert.AreEqual(Lifecycle.Active, ex.CurrentLifecycle);
        Assert.AreEqual("Decommission", ex.AttemptedTransition);
        Assert.AreEqual(sunsetDate, ex.SunsetDate);
        Assert.AreEqual("before-sunset-date", ex.Reason);
    }

    [TestMethod]
    [DataRow(Lifecycle.Active, "active")]
    [DataRow(Lifecycle.Deprecated, "deprecated")]
    [DataRow(Lifecycle.Decommissioned, "decommissioned")]
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

        Assert.AreEqual(expectedWire, ((ILifecycleConflict)ex).CurrentLifecycleName);
    }
}
