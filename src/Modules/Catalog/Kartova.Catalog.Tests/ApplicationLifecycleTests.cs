using System.Text.RegularExpressions;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

// NOTE: A `using Kartova.Catalog.Domain;` would not bring `Application` into scope
// unambiguously here — the enclosing `Kartova.Catalog` namespace contains a sibling
// child namespace `Kartova.Catalog.Application` which wins simple-name lookup. We
// therefore alias the type explicitly. (Mirrors ApplicationTests.cs.)
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ApplicationLifecycleTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null) => TestClocks.At(now ?? Now);

    private static DomainApplication NewActive() =>
        DomainApplication.Create("payments-api", "Payments API", "Description.", Owner, Tenant, Clock());

    [TestMethod]
    public void New_application_starts_in_Active_state_with_null_sunsetDate()
    {
        var app = NewActive();
        Assert.AreEqual(Lifecycle.Active, app.Lifecycle);
        Assert.IsNull(app.SunsetDate);
    }

    [TestMethod]
    public void EditMetadata_with_valid_args_updates_displayName_and_description()
    {
        var app = NewActive();
        app.EditMetadata("New Display", "New description.");
        Assert.AreEqual("New Display", app.DisplayName);
        Assert.AreEqual("New description.", app.Description);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void EditMetadata_throws_on_empty_displayName(string displayName)
    {
        var app = NewActive();
        var ex = Assert.ThrowsExactly<ArgumentException>(() => app.EditMetadata(displayName, "desc"));
        StringAssert.Contains(ex.Message, "display name");
    }

    [TestMethod]
    public void EditMetadata_throws_on_displayName_over_128()
    {
        var app = NewActive();
        var tooLong = new string('x', 129);
        var ex = Assert.ThrowsExactly<ArgumentException>(() => app.EditMetadata(tooLong, "desc"));
        StringAssert.Contains(ex.Message, "128");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void EditMetadata_throws_on_empty_description(string description)
    {
        var app = NewActive();
        var ex = Assert.ThrowsExactly<ArgumentException>(() => app.EditMetadata("Display", description));
        StringAssert.Contains(ex.Message, "description");
    }

    [TestMethod]
    public void EditMetadata_does_not_change_Name_or_OwnerUserId_or_TenantId_or_CreatedAt()
    {
        var app = NewActive();
        var origName = app.Name;
        var origOwner = app.OwnerUserId;
        var origTenant = app.TenantId;
        var origCreated = app.CreatedAt;

        app.EditMetadata("Different", "Different.");

        Assert.AreEqual(origName, app.Name);
        Assert.AreEqual(origOwner, app.OwnerUserId);
        Assert.AreEqual(origTenant, app.TenantId);
        Assert.AreEqual(origCreated, app.CreatedAt);
    }

    [TestMethod]
    public void EditMetadata_on_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(() => app.EditMetadata("X", "Y"));
        Assert.AreEqual(Lifecycle.Decommissioned, ex.CurrentLifecycle);
    }

    [TestMethod]
    public void EditMetadata_on_Deprecated_succeeds()
    {
        // Spec §9.8 step 5: Deprecated still allows edit. The terminal-write
        // guard only fires on Decommissioned. A mutation that flipped the
        // guard from `Lifecycle == Decommissioned` to `Lifecycle != Active`
        // would silently break editing for every Deprecated app — this test
        // is the positive allow-path that catches it.
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());

        app.EditMetadata("New Display", "New description.");

        Assert.AreEqual(Lifecycle.Deprecated, app.Lifecycle);
        Assert.AreEqual("New Display", app.DisplayName);
        Assert.AreEqual("New description.", app.Description);
    }

    [TestMethod]
    public void Deprecate_with_valid_args_sets_state_and_sunsetDate()
    {
        var app = NewActive();
        var sunset = Now.AddDays(30);
        app.Deprecate(sunset, Clock());

        Assert.AreEqual(Lifecycle.Deprecated, app.Lifecycle);
        Assert.AreEqual(sunset, app.SunsetDate);
    }

    [TestMethod]
    public void Deprecate_throws_on_past_sunsetDate()
    {
        var app = NewActive();
        var ex = Assert.ThrowsExactly<ArgumentException>(() => app.Deprecate(Now.AddDays(-1), Clock()));
        // FA's "*sunset*future*" glob (two segments) translated to a regex requiring both substrings.
        StringAssert.Matches(ex.Message, new Regex("sunset.*future"));
    }

    [TestMethod]
    public void Deprecate_throws_on_now_sunsetDate()
    {
        var app = NewActive();
        var ex = Assert.ThrowsExactly<ArgumentException>(() => app.Deprecate(Now, Clock()));
        StringAssert.Matches(ex.Message, new Regex("sunset.*future"));
    }

    [TestMethod]
    public void Deprecate_when_already_Deprecated_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(30), Clock());

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.Deprecate(Now.AddDays(60), Clock()));
        Assert.AreEqual(Lifecycle.Deprecated, ex.CurrentLifecycle);
    }

    [TestMethod]
    public void Deprecate_when_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.Deprecate(Now.AddDays(30), Clock(Now.AddDays(3))));
        Assert.AreEqual(Lifecycle.Decommissioned, ex.CurrentLifecycle);
    }

    [TestMethod]
    public void Decommission_when_Deprecated_and_after_sunsetDate_succeeds()
    {
        var app = NewActive();
        var sunset = Now.AddDays(1);
        app.Deprecate(sunset, Clock());
        app.Decommission(Clock(sunset));            // exact sunset — boundary uses >=

        Assert.AreEqual(Lifecycle.Decommissioned, app.Lifecycle);
        Assert.AreEqual(sunset, app.SunsetDate);    // sunset preserved on transition
    }

    [TestMethod]
    public void Decommission_when_Deprecated_and_before_sunsetDate_throws_with_reason_before_sunset_date()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(30), Clock());

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.Decommission(Clock(Now.AddDays(15))));
        Assert.AreEqual("before-sunset-date", ex.Reason);
    }

    [TestMethod]
    public void Decommission_when_Active_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(() => app.Decommission(Clock()));
        Assert.AreEqual(Lifecycle.Active, ex.CurrentLifecycle);
    }

    [TestMethod]
    public void Decommission_when_already_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.Decommission(Clock(Now.AddDays(3))));
        Assert.AreEqual(Lifecycle.Decommissioned, ex.CurrentLifecycle);
    }
}
