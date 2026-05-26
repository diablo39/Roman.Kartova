using Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authorization;

namespace Kartova.SharedKernel.AspNetCore;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Registers one ASP.NET authorization policy per <see cref="KartovaPermissions"/> entry.
    /// Policy name equals the permission name; body is <c>RequireClaim(KartovaClaims.Permission, &lt;perm&gt;)</c>.
    /// </summary>
    public static AuthorizationBuilder AddKartovaPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var perm in KartovaPermissions.All)
        {
            builder.AddPolicy(perm, p => p.RequireClaim(KartovaClaims.Permission, perm));
        }
        return builder;
    }

    /// <summary>
    /// Registers resource-based authorization policies for team-scoped operations (slice 8).
    /// Each policy carries a requirement matched by a registered <see cref="IAuthorizationHandler"/>
    /// against a resource implementing <see cref="ITeamScopedResource"/> or <see cref="ITeamOwnedResource"/>.
    /// </summary>
    public static AuthorizationBuilder AddKartovaResourcePolicies(this AuthorizationBuilder builder)
    {
        builder.AddPolicy(KartovaTeamPolicies.ApplicationTeamScoped, p =>
            p.Requirements.Add(new ApplicationTeamScopedRequirement()));
        builder.AddPolicy(KartovaTeamPolicies.TeamAdminOfThis, p =>
            p.Requirements.Add(new TeamAdminOfThisRequirement()));
        return builder;
    }
}
