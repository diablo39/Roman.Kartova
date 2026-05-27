using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Exposes the current authenticated user's identity from the request context.
/// Caller must run inside the auth pipeline — accessing properties when no user
/// is authenticated throws. Registered as <see cref="ServiceLifetime.Scoped"/>
/// by <see cref="JwtAuthenticationExtensions.AddKartovaJwtAuth"/>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Guid form of the JWT 'sub' claim. KeyCloak issues UUIDs for user IDs.
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// Team memberships for the current user within the current tenant scope.
    /// Sourced from <see cref="ITenantContext"/> and populated by the auth
    /// pipeline. Empty when no memberships exist.
    /// </summary>
    IReadOnlyList<TeamMembershipInfo> TeamMemberships { get; }

    /// <summary>
    /// Convenience projection of <see cref="TeamMemberships"/> to a set of team ids.
    /// </summary>
    IReadOnlySet<Guid> TeamIds { get; }

    /// <summary>
    /// Set by the post-auth sync hook when the current request's caller has just accepted
    /// an invitation in the same request that triggered claim-sync; <c>null</c> otherwise.
    /// </summary>
    Guid? JustAcceptedInvitationId { get; }
}
