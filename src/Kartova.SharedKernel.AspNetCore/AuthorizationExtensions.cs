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
}
