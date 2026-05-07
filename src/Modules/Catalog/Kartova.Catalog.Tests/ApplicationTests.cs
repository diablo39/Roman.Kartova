using FluentAssertions;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

// NOTE: A `using Kartova.Catalog.Domain;` would not bring `Application` into scope
// unambiguously here — the enclosing `Kartova.Catalog` namespace contains a sibling
// child namespace `Kartova.Catalog.Application` which wins simple-name lookup. We
// therefore alias the type explicitly.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

public class ApplicationTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now ?? Now);
        return c;
    }

    [Fact]
    public void Create_with_valid_args_returns_application()
    {
        var app = DomainApplication.Create("payments-api", "Payments API", "Payments REST surface.", Owner, Tenant, Clock());

        app.Name.Should().Be("payments-api");
        app.DisplayName.Should().Be("Payments API");
        app.Description.Should().Be("Payments REST surface.");
        app.OwnerUserId.Should().Be(Owner);
        app.TenantId.Should().Be(Tenant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_throws_on_empty_or_whitespace_name(string name)
    {
        var act = () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_throws_ArgumentException_with_empty_message_for_blank_name(string emptyName)
    {
        // Kills mutant at line 87: `throw new ArgumentException("Application name must not be empty.", ...)` mutated to `;`.
        // With the throw removed, empty/whitespace names fall through to the kebab-case check which throws a
        // DIFFERENT message ("kebab-case"). Asserting on "empty" in the message pins the specific guard.
        var act = () => DomainApplication.Create(emptyName, "Display Name", "desc", Owner, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public void Create_throws_on_name_over_256_chars()
    {
        var name = new string('x', 257);
        var act = () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*256*");
    }

    [Theory]
    [InlineData("BadName")]            // uppercase
    [InlineData("bad name")]           // space
    [InlineData("bad_name")]           // underscore
    [InlineData("bad--name")]          // double dash
    [InlineData("-leading")]           // leading dash
    [InlineData("trailing-")]          // trailing dash
    [InlineData("9digit")]             // leading digit
    [InlineData("Mixed-Case")]         // mixed case
    [InlineData("kebab.with.dot")]     // dot
    public void Create_throws_on_non_kebab_case_name(string name)
    {
        var act = () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*kebab-case*");
    }

    [Theory]
    [InlineData("a")]                  // single letter
    [InlineData("abc")]                // single segment
    [InlineData("payment-gateway")]    // canonical form
    [InlineData("a1")]                 // letter + digit
    [InlineData("a-b-c-d")]            // many segments
    [InlineData("v2-api")]             // segment with digit
    public void Create_succeeds_with_kebab_case_name(string name)
    {
        var app = DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock());
        app.Name.Should().Be(name);
    }

    [Fact]
    public void Create_succeeds_with_name_at_exactly_256_chars()
    {
        // Boundary pin — the invariant is `length > 256 throws`, so 256 must succeed.
        // Without this test the off-by-one mutation `length >= 256` survives.
        var name = new string('x', 256);
        var app = DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant, Clock());
        app.Name.Should().HaveLength(256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_empty_or_whitespace_displayName(string displayName)
    {
        var act = () => DomainApplication.Create("name", displayName, "desc", Owner, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*display name*");
    }

    [Fact]
    public void Create_throws_on_displayName_over_128_chars()
    {
        var displayName = new string('x', 129);
        var act = () => DomainApplication.Create("name", displayName, "desc", Owner, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*128*");
    }

    [Fact]
    public void Create_succeeds_on_displayName_at_128_chars()
    {
        var displayName = new string('x', 128);
        var app = DomainApplication.Create("name", displayName, "desc", Owner, Tenant, Clock());
        app.DisplayName.Should().Be(displayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_empty_or_whitespace_description(string description)
    {
        var act = () => DomainApplication.Create("name", "Display Name", description, Owner, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*description*");
    }

    [Fact]
    public void Create_throws_on_empty_owner_user_id()
    {
        var act = () => DomainApplication.Create("name", "Display Name", "desc", Guid.Empty, Tenant, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*ownerUserId*");
    }

    [Fact]
    public void Create_assigns_fresh_id_each_call()
    {
        var a = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, Clock());
        var b = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, Clock());
        a.Id.Should().NotBe(b.Id);
        a.Id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_uses_clock_GetUtcNow_for_CreatedAt()
    {
        var clock = Clock();

        var app = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, clock);

        app.CreatedAt.Should().Be(clock.GetUtcNow());
        app.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Create_with_null_clock_throws()
    {
        var act = () => DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant, clock: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }
}
