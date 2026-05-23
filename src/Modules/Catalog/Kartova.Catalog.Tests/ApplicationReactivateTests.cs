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
public sealed class ApplicationReactivateTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 5, 22, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null) => TestClocks.At(now ?? Now);

    private static DomainApplication NewDeprecated(DateTimeOffset sunsetDate)
    {
        var app = DomainApplication.Create("my-app", "My App", "Desc.", Owner, Tenant, Clock());
        app.Deprecate(sunsetDate, Clock());
        return app;
    }

    private static DomainApplication NewDecommissioned()
    {
        var app = DomainApplication.Create("my-app", "My App", "Desc.", Owner, Tenant, Clock());
        app.Deprecate(Now.AddDays(7), Clock());
        app.Decommission(Clock(Now.AddDays(8)));
        return app;
    }

    [TestMethod]
    public void Reactivate_from_Deprecated_returns_to_Active_and_clears_sunset_date()
    {
        var app = NewDeprecated(sunsetDate: Now.AddDays(30));

        app.Reactivate();

        Assert.AreEqual(Lifecycle.Active, app.Lifecycle);
        Assert.IsNull(app.SunsetDate);
    }

    [TestMethod]
    public void Reactivate_from_Decommissioned_returns_to_Active_and_clears_sunset_date()
    {
        var app = NewDecommissioned();

        app.Reactivate();

        Assert.AreEqual(Lifecycle.Active, app.Lifecycle);
        Assert.IsNull(app.SunsetDate);
    }

    [TestMethod]
    public void Reactivate_from_Active_throws_InvalidLifecycleTransitionException()
    {
        var app = DomainApplication.Create("my-app", "My App", "Desc.", Owner, Tenant, Clock());

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(() => app.Reactivate());
        Assert.AreEqual(Lifecycle.Active, ex.CurrentLifecycle);
        Assert.AreEqual(nameof(DomainApplication.Reactivate), ex.AttemptedTransition);
    }
}
