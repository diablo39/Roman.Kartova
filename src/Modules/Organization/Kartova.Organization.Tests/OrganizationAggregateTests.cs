using FluentAssertions;
using Xunit;

namespace Kartova.Organization.Tests;

public class OrganizationAggregateTests
{
    [Fact]
    public void Create_with_valid_name_sets_tenant_id_equal_to_id()
    {
        var org = Domain.Organization.Create("Acme");

        org.Id.Value.Should().NotBeEmpty();
        org.TenantId.Value.Should().Be(org.Id.Value);
        org.Name.Should().Be("Acme");
        org.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_empty_name_throws(string? name)
    {
        var act = () => Domain.Organization.Create(name!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_too_long_name_throws()
    {
        var name = new string('a', 101);
        var act = () => Domain.Organization.Create(name);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_updates_name()
    {
        var org = Domain.Organization.Create("Acme");
        org.Rename("NewName");
        org.Name.Should().Be("NewName");
    }
}
