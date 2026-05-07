using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Kartova.Organization.Tests;

public class OrganizationAggregateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now ?? Now);
        return c;
    }

    [Fact]
    public void Create_with_valid_name_sets_tenant_id_equal_to_id_and_uses_clock_for_CreatedAt()
    {
        var clock = Clock();

        var org = Domain.Organization.Create("Acme", clock);

        org.Id.Value.Should().NotBeEmpty();
        org.TenantId.Value.Should().Be(org.Id.Value);
        org.Name.Should().Be("Acme");
        org.CreatedAt.Should().Be(clock.GetUtcNow());
    }

    [Fact]
    public void Create_with_null_clock_throws()
    {
        var act = () => Domain.Organization.Create("Acme", clock: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_empty_name_throws(string? name)
    {
        var act = () => Domain.Organization.Create(name!, Clock());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_too_long_name_throws()
    {
        var name = new string('a', 101);
        var act = () => Domain.Organization.Create(name, Clock());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_updates_name()
    {
        var org = Domain.Organization.Create("Acme", Clock());
        org.Rename("NewName");
        org.Name.Should().Be("NewName");
    }
}
