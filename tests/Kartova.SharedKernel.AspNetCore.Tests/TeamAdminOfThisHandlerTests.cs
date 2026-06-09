using System.Security.Claims;
using Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public sealed class TeamAdminOfThisHandlerTests
{
    private sealed class FakeTeam : ITeamOwnedResource
    {
        public Guid TeamId { get; init; }
    }

    [TestMethod]
    public async Task OrgAdmin_always_succeeds()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamMemberships.Returns(Array.Empty<TeamMembershipInfo>());

        var sut = new TeamAdminOfThisHandler(currentUser);
        var requirement = new TeamAdminOfThisRequirement();
        var resource = new FakeTeam { TeamId = Guid.NewGuid() };

        var principal = MakePrincipal(KartovaRoles.OrgAdmin);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsTrue(ctx.HasSucceeded);
    }

    [TestMethod]
    public async Task Member_realm_role_with_Admin_membership_succeeds()
    {
        var teamId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamMemberships.Returns(new[] { new TeamMembershipInfo(teamId, TeamRoleKind.Admin) });

        var sut = new TeamAdminOfThisHandler(currentUser);
        var requirement = new TeamAdminOfThisRequirement();
        var resource = new FakeTeam { TeamId = teamId };

        var principal = MakePrincipal(KartovaRoles.Member);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsTrue(ctx.HasSucceeded);
    }

    [TestMethod]
    public async Task Member_of_team_but_not_Admin_fails()
    {
        var teamId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamMemberships.Returns(new[] { new TeamMembershipInfo(teamId, TeamRoleKind.Member) });

        var sut = new TeamAdminOfThisHandler(currentUser);
        var requirement = new TeamAdminOfThisRequirement();
        var resource = new FakeTeam { TeamId = teamId };

        var principal = MakePrincipal(KartovaRoles.Member);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsFalse(ctx.HasSucceeded);
    }

    [TestMethod]
    public async Task Member_with_Admin_membership_of_another_team_fails()
    {
        var otherTeamId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamMemberships.Returns(new[] { new TeamMembershipInfo(otherTeamId, TeamRoleKind.Admin) });

        var sut = new TeamAdminOfThisHandler(currentUser);
        var requirement = new TeamAdminOfThisRequirement();
        var resource = new FakeTeam { TeamId = Guid.NewGuid() };   // a DIFFERENT team

        var principal = MakePrincipal(KartovaRoles.Member);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsFalse(ctx.HasSucceeded);
    }

    [TestMethod]
    public async Task Member_with_no_membership_fails()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamMemberships.Returns(Array.Empty<TeamMembershipInfo>());

        var sut = new TeamAdminOfThisHandler(currentUser);
        var requirement = new TeamAdminOfThisRequirement();
        var resource = new FakeTeam { TeamId = Guid.NewGuid() };

        var principal = MakePrincipal(KartovaRoles.Member);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsFalse(ctx.HasSucceeded);
    }

    private static ClaimsPrincipal MakePrincipal(string role)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test");
        return new ClaimsPrincipal(identity);
    }
}
