using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

// NOTE: A `using Kartova.Catalog.Domain;` would not bring `Application` into scope
// unambiguously here — the enclosing `Kartova.Catalog` namespace contains a sibling
// child namespace `Kartova.Catalog.Application` which wins simple-name lookup. We
// therefore alias the type explicitly.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApplicationUnDecommissionTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Team = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 5, 22, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null) => TestClocks.At(now ?? Now);

    private static (DomainApplication app, FakeTimeProvider clock) NewDecommissioned()
    {
        var clock = new FakeTimeProvider(Now);
        var app = DomainApplication.Create("My App", "Desc.", Creator, Team, Tenant, clock);
        app.Deprecate(Now.AddDays(7), clock);
        clock.SetUtcNow(Now.AddDays(8));
        app.Decommission(clock);
        return (app, clock);
    }

    [TestMethod]
    public void UnDecommission_from_Decommissioned_returns_to_Deprecated_with_new_sunset_date()
    {
        var (app, clock) = NewDecommissioned();
        var newSunset = clock.GetUtcNow().AddDays(30);

        app.UnDecommission(newSunset, clock);

        Assert.AreEqual(Lifecycle.Deprecated, app.Lifecycle);
        Assert.AreEqual(newSunset, app.SunsetDate);
    }

    [TestMethod]
    public void UnDecommission_from_Active_throws_InvalidLifecycleTransitionException()
    {
        var app = DomainApplication.Create("My App", "Desc.", Creator, Team, Tenant, Clock());

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.UnDecommission(Now.AddDays(30), Clock()));

        Assert.AreEqual(Lifecycle.Active, ex.CurrentLifecycle);
        Assert.AreEqual(nameof(DomainApplication.UnDecommission), ex.AttemptedTransition);
    }

    [TestMethod]
    public void UnDecommission_from_Deprecated_throws_InvalidLifecycleTransitionException()
    {
        var app = DomainApplication.Create("My App", "Desc.", Creator, Team, Tenant, Clock());
        app.Deprecate(Now.AddDays(7), Clock());

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.UnDecommission(Now.AddDays(30), Clock()));

        Assert.AreEqual(Lifecycle.Deprecated, ex.CurrentLifecycle);
        Assert.AreEqual(nameof(DomainApplication.UnDecommission), ex.AttemptedTransition);
    }

    [TestMethod]
    public void UnDecommission_with_past_sunset_date_throws_ArgumentException()
    {
        var (app, clock) = NewDecommissioned();
        var pastDate = clock.GetUtcNow().AddDays(-1);

        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => app.UnDecommission(pastDate, clock));

        Assert.AreEqual("newSunsetDate", ex.ParamName);
    }

    [TestMethod]
    public void UnDecommission_with_sunset_date_equal_to_now_throws_ArgumentException()
    {
        var (app, clock) = NewDecommissioned();
        var exactNow = clock.GetUtcNow();

        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => app.UnDecommission(exactNow, clock));

        Assert.AreEqual("newSunsetDate", ex.ParamName);
    }
}
