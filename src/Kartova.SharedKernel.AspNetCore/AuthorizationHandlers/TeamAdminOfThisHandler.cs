using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authorization;

namespace Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;

public sealed class TeamAdminOfThisHandler(ICurrentUser currentUser)
    : AuthorizationHandler<TeamAdminOfThisRequirement, ITeamOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TeamAdminOfThisRequirement requirement,
        ITeamOwnedResource resource)
    {
        if (context.User.IsInRole(KartovaRoles.OrgAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (currentUser.TeamMemberships.Any(m =>
                m.TeamId == resource.TeamId && m.Role == TeamRoleKind.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
