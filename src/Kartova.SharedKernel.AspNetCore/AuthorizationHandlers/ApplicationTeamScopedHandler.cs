using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authorization;

namespace Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;

public sealed class ApplicationTeamScopedHandler(ICurrentUser currentUser)
    : AuthorizationHandler<ApplicationTeamScopedRequirement, ITeamScopedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApplicationTeamScopedRequirement requirement,
        ITeamScopedResource resource)
    {
        if (context.User.IsInRole(KartovaRoles.OrgAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (resource.TeamId is null) return Task.CompletedTask;

        if (currentUser.TeamIds.Contains(resource.TeamId.Value))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
