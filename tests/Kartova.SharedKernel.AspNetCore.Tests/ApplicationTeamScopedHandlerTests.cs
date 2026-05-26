using System.Security.Claims;
using Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public sealed class ApplicationTeamScopedHandlerTests
{
    private sealed class FakeApp : ITeamScopedResource
    {
        public Guid? TeamId { get; init; }
    }

    [TestMethod]
    public async Task OrgAdmin_always_succeeds_even_on_unassigned_app()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        // OrgAdmin's team-membership doesn't matter — empty.
        currentUser.TeamIds.Returns(new HashSet<Guid>());

        var sut = new ApplicationTeamScopedHandler(currentUser);
        var requirement = new ApplicationTeamScopedRequirement();
        var resource = new FakeApp { TeamId = null };

        var principal = MakePrincipal(KartovaRoles.OrgAdmin);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsTrue(ctx.HasSucceeded);
    }

    [TestMethod]
    public async Task Non_OrgAdmin_on_unassigned_app_fails()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamIds.Returns(new HashSet<Guid> { Guid.NewGuid() });

        var sut = new ApplicationTeamScopedHandler(currentUser);
        var requirement = new ApplicationTeamScopedRequirement();
        var resource = new FakeApp { TeamId = null };

        var principal = MakePrincipal(KartovaRoles.Member);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsFalse(ctx.HasSucceeded);
    }

    [TestMethod]
    public async Task Member_in_apps_team_succeeds()
    {
        var teamId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamIds.Returns(new HashSet<Guid> { teamId });

        var sut = new ApplicationTeamScopedHandler(currentUser);
        var requirement = new ApplicationTeamScopedRequirement();
        var resource = new FakeApp { TeamId = teamId };

        var principal = MakePrincipal(KartovaRoles.Member);
        var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

        await ((IAuthorizationHandler)sut).HandleAsync(ctx);

        Assert.IsTrue(ctx.HasSucceeded);
    }

    [TestMethod]
    public async Task Member_not_in_apps_team_fails()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamIds.Returns(new HashSet<Guid> { Guid.NewGuid() });   // user is in some other team

        var sut = new ApplicationTeamScopedHandler(currentUser);
        var requirement = new ApplicationTeamScopedRequirement();
        var resource = new FakeApp { TeamId = Guid.NewGuid() };   // app is in yet another team

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
