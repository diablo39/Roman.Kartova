using Kartova.Organization.Domain;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class TeamMembershipTests
{
    [TestMethod]
    public void Create_with_valid_inputs_sets_properties()
    {
        var teamId = TeamId.New();
        var userId = Guid.NewGuid();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var m = TeamMembership.Create(teamId, userId, TeamRole.Admin, clock);

        Assert.AreEqual(teamId, m.TeamId);
        Assert.AreEqual(userId, m.UserId);
        Assert.AreEqual(TeamRole.Admin, m.Role);
        Assert.AreEqual(clock.GetUtcNow(), m.AddedAt);
    }

    [TestMethod]
    public void Create_with_empty_user_id_throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            TeamMembership.Create(TeamId.New(), Guid.Empty, TeamRole.Member, new FakeTimeProvider(DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public void ChangeRole_updates_role()
    {
        var m = TeamMembership.Create(TeamId.New(), Guid.NewGuid(), TeamRole.Member,
            new FakeTimeProvider(DateTimeOffset.UtcNow));
        m.ChangeRole(TeamRole.Admin);
        Assert.AreEqual(TeamRole.Admin, m.Role);
    }
}
