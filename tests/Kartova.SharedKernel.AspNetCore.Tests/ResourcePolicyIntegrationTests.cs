using System.Security.Claims;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public sealed class ResourcePolicyIntegrationTests
{
    [TestMethod]
    public async Task AuthorizeAsync_resolves_handler_by_interface_match()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddKartovaResourcePolicies();
        services.AddScoped<IAuthorizationHandler, ApplicationTeamScopedHandler>();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TeamIds.Returns(new HashSet<Guid>());
        services.AddSingleton(currentUser);

        var sp = services.BuildServiceProvider();
        var auth = sp.GetRequiredService<IAuthorizationService>();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, KartovaRoles.OrgAdmin),
        }, "test"));

        // Concrete type that implements ITeamScopedResource — handler must match by interface
        var fakeApp = new FakeAppResource { TeamId = null };
        var result = await auth.AuthorizeAsync(principal, fakeApp, KartovaTeamPolicies.ApplicationTeamScoped);

        Assert.IsTrue(result.Succeeded, "OrgAdmin should succeed; this also proves handler dispatch via interface.");
    }

    private sealed class FakeAppResource : ITeamScopedResource
    {
        public Guid? TeamId { get; init; }
    }
}
