using FluentAssertions;
using Kartova.SharedKernel.Multitenancy;

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

    [Fact]
    public void Create_with_valid_args_returns_application()
    {
        var app = DomainApplication.Create("payments-api", "Payments API", "Payments REST surface.", Owner, Tenant);

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
        var act = () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void Create_throws_on_name_over_256_chars()
    {
        var name = new string('x', 257);
        var act = () => DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*256*");
    }

    [Fact]
    public void Create_succeeds_with_name_at_exactly_256_chars()
    {
        // Boundary pin — the invariant is `length > 256 throws`, so 256 must succeed.
        // Without this test the off-by-one mutation `length >= 256` survives.
        var name = new string('x', 256);
        var app = DomainApplication.Create(name, "Display Name", "desc", Owner, Tenant);
        app.Name.Should().HaveLength(256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_empty_or_whitespace_displayName(string displayName)
    {
        var act = () => DomainApplication.Create("name", displayName, "desc", Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*display name*");
    }

    [Fact]
    public void Create_throws_on_displayName_over_128_chars()
    {
        var displayName = new string('x', 129);
        var act = () => DomainApplication.Create("name", displayName, "desc", Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*128*");
    }

    [Fact]
    public void Create_succeeds_on_displayName_at_128_chars()
    {
        var displayName = new string('x', 128);
        var app = DomainApplication.Create("name", displayName, "desc", Owner, Tenant);
        app.DisplayName.Should().Be(displayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_empty_or_whitespace_description(string description)
    {
        var act = () => DomainApplication.Create("name", "Display Name", description, Owner, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*description*");
    }

    [Fact]
    public void Create_throws_on_empty_owner_user_id()
    {
        var act = () => DomainApplication.Create("name", "Display Name", "desc", Guid.Empty, Tenant);
        act.Should().Throw<ArgumentException>().WithMessage("*ownerUserId*");
    }

    [Fact]
    public void Create_assigns_fresh_id_each_call()
    {
        var a = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant);
        var b = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant);
        a.Id.Should().NotBe(b.Id);
        a.Id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_assigns_recent_utc_CreatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var app = DomainApplication.Create("name", "Display Name", "desc", Owner, Tenant);
        var after = DateTimeOffset.UtcNow;
        app.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        app.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }
}
