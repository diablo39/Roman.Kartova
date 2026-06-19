using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Infrastructure.Tests;

[TestClass]
public class CatalogAuditEntriesTests
{
    private static DomainApplication NewDeprecatedApp(out DateTimeOffset sunset)
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(DateTimeOffset.Parse("2026-06-19T10:00:00Z"));
        // Application.Create(displayName, description, createdByUserId, teamId, tenantId, clock)
        var app = DomainApplication.Create(
            "App", "Desc", Guid.NewGuid(), Guid.NewGuid(), new TenantId(Guid.NewGuid()), clock);
        sunset = clock.GetUtcNow().AddDays(30);
        app.Deprecate(sunset, clock);
        return app;
    }

    [TestMethod]
    public void LifecycleChanged_CapturesFromToAndSunsetDate()
    {
        var app = NewDeprecatedApp(out var sunset);

        var entry = CatalogAuditEntries.LifecycleChanged(app, from: Lifecycle.Active);

        Assert.AreEqual(CatalogAuditActions.ApplicationLifecycleChanged, entry.Action);
        Assert.AreEqual(CatalogAuditTargetTypes.Application, entry.TargetType);
        Assert.AreEqual(app.Id.Value.ToString(), entry.TargetId);
        Assert.AreEqual("Active", entry.Data!["from"]);
        Assert.AreEqual("Deprecated", entry.Data!["to"]);
        Assert.AreEqual(sunset.ToString("O"), entry.Data!["sunsetDate"]);
    }

    [TestMethod]
    public void LifecycleChanged_NullSunsetDate_SerializesAsNull()
    {
        var app = NewDeprecatedApp(out _);
        app.Reactivate(); // clears SunsetDate, lifecycle -> Active

        var entry = CatalogAuditEntries.LifecycleChanged(app, from: Lifecycle.Deprecated);

        Assert.AreEqual("Deprecated", entry.Data!["from"]);
        Assert.AreEqual("Active", entry.Data!["to"]);
        Assert.IsNull(entry.Data!["sunsetDate"]);
    }
}
