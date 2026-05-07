using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

// NOTE: A `using Kartova.Catalog.Domain;` would not bring `Application` into scope
// unambiguously here — the enclosing `Kartova.Catalog` namespace contains a sibling
// child namespace `Kartova.Catalog.Application` which wins simple-name lookup. We
// therefore alias the type explicitly. (Mirrors ApplicationTests.cs.)
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

public class ApplicationLifecycleTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now ?? Now);
        return c;
    }

    private static DomainApplication NewActive() =>
        DomainApplication.Create("payments-api", "Payments API", "Description.", Owner, Tenant, Clock());

    [Fact]
    public void New_application_starts_in_Active_state_with_null_sunsetDate()
    {
        var app = NewActive();
        app.Lifecycle.Should().Be(Lifecycle.Active);
        app.SunsetDate.Should().BeNull();
    }

    [Fact]
    public void EditMetadata_with_valid_args_updates_displayName_and_description()
    {
        var app = NewActive();
        app.EditMetadata("New Display", "New description.");
        app.DisplayName.Should().Be("New Display");
        app.Description.Should().Be("New description.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EditMetadata_throws_on_empty_displayName(string displayName)
    {
        var app = NewActive();
        var act = () => app.EditMetadata(displayName, "desc");
        act.Should().Throw<ArgumentException>().WithMessage("*display name*");
    }

    [Fact]
    public void EditMetadata_throws_on_displayName_over_128()
    {
        var app = NewActive();
        var tooLong = new string('x', 129);
        var act = () => app.EditMetadata(tooLong, "desc");
        act.Should().Throw<ArgumentException>().WithMessage("*128*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EditMetadata_throws_on_empty_description(string description)
    {
        var app = NewActive();
        var act = () => app.EditMetadata("Display", description);
        act.Should().Throw<ArgumentException>().WithMessage("*description*");
    }

    [Fact]
    public void EditMetadata_does_not_change_Name_or_OwnerUserId_or_TenantId_or_CreatedAt()
    {
        var app = NewActive();
        var origName = app.Name;
        var origOwner = app.OwnerUserId;
        var origTenant = app.TenantId;
        var origCreated = app.CreatedAt;

        app.EditMetadata("Different", "Different.");

        app.Name.Should().Be(origName);
        app.OwnerUserId.Should().Be(origOwner);
        app.TenantId.Should().Be(origTenant);
        app.CreatedAt.Should().Be(origCreated);
    }

    [Fact]
    public void EditMetadata_on_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var act = () => app.EditMetadata("X", "Y");
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Decommissioned);
    }

    [Fact]
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

        app.Lifecycle.Should().Be(Lifecycle.Deprecated);
        app.DisplayName.Should().Be("New Display");
        app.Description.Should().Be("New description.");
    }

    [Fact]
    public void Deprecate_with_valid_args_sets_state_and_sunsetDate()
    {
        var app = NewActive();
        var sunset = Now.AddDays(30);
        app.Deprecate(sunset, Clock());

        app.Lifecycle.Should().Be(Lifecycle.Deprecated);
        app.SunsetDate.Should().Be(sunset);
    }

    [Fact]
    public void Deprecate_throws_on_past_sunsetDate()
    {
        var app = NewActive();
        var act = () => app.Deprecate(Now.AddDays(-1), Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*sunset*future*");
    }

    [Fact]
    public void Deprecate_throws_on_now_sunsetDate()
    {
        var app = NewActive();
        var act = () => app.Deprecate(Now, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*sunset*future*");
    }

    [Fact]
    public void Deprecate_when_already_Deprecated_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(30), Clock());

        var act = () => app.Deprecate(Now.AddDays(60), Clock());
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Deprecated);
    }

    [Fact]
    public void Deprecate_when_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var act = () => app.Deprecate(Now.AddDays(30), Clock(Now.AddDays(3)));
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Decommissioned);
    }

    [Fact]
    public void Decommission_when_Deprecated_and_after_sunsetDate_succeeds()
    {
        var app = NewActive();
        var sunset = Now.AddDays(1);
        app.Deprecate(sunset, Clock());
        app.Decommission(Clock(sunset));            // exact sunset — boundary uses >=

        app.Lifecycle.Should().Be(Lifecycle.Decommissioned);
        app.SunsetDate.Should().Be(sunset);         // sunset preserved on transition
    }

    [Fact]
    public void Decommission_when_Deprecated_and_before_sunsetDate_throws_with_reason_before_sunset_date()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(30), Clock());

        var act = () => app.Decommission(Clock(Now.AddDays(15)));
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.Reason.Should().Be("before-sunset-date");
    }

    [Fact]
    public void Decommission_when_Active_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        var act = () => app.Decommission(Clock());
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Active);
    }

    [Fact]
    public void Decommission_when_already_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var act = () => app.Decommission(Clock(Now.AddDays(3)));
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Decommissioned);
    }
}
