using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class TeamTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    [TestMethod]
    public void Create_with_valid_inputs_sets_properties()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var team = Team.Create("Platform", "Owns infra and tooling", Tenant, clock);

        Assert.AreEqual("Platform", team.DisplayName);
        Assert.AreEqual("Owns infra and tooling", team.Description);
        Assert.AreEqual(Tenant, team.TenantId);
        Assert.AreEqual(clock.GetUtcNow(), team.CreatedAt);
        Assert.AreNotEqual(Guid.Empty, team.Id.Value);
    }

    [TestMethod]
    public void Create_with_null_description_is_allowed()
    {
        var team = Team.Create("Platform", null, Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow));
        Assert.IsNull(team.Description);
    }

    [TestMethod]
    public void Create_with_empty_display_name_throws()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Assert.ThrowsExactly<ArgumentException>(() => Team.Create("", null, Tenant, clock));
        Assert.ThrowsExactly<ArgumentException>(() => Team.Create("   ", null, Tenant, clock));
    }

    [TestMethod]
    public void Create_with_too_long_display_name_throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            Team.Create(new string('a', 129), null, Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public void Create_with_too_long_description_throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            Team.Create("Platform", new string('a', 513), Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public void Rename_updates_display_name_and_description()
    {
        var team = Team.Create("Platform", "Initial", Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow));
        team.Rename("Platform v2", "Updated");
        Assert.AreEqual("Platform v2", team.DisplayName);
        Assert.AreEqual("Updated", team.Description);
    }

    [TestMethod]
    public void Team_implements_ITenantOwned_and_ITeamOwnedResource()
    {
        var team = Team.Create("Platform", null, Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow));
        Assert.IsInstanceOfType<ITenantOwned>(team);
        Assert.IsInstanceOfType<ITeamOwnedResource>(team);
        Assert.AreEqual(team.Id.Value, ((ITeamOwnedResource)team).TeamId);
    }
}
