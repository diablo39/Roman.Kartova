using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Payload nested inside <see cref="SessionStartResponse"/> when the current
/// request's authenticated principal has just flipped a Pending invitation to
/// Accepted in the same request (slice 9 spec §6.7 / §9.4). The SPA reads this
/// to render a one-time welcome screen — it is <c>null</c> on subsequent
/// session-start calls because <see cref="Kartova.SharedKernel.AspNetCore.ICurrentUser.JustAcceptedInvitationId"/>
/// is only set in the request that performed the acceptance side-effect.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record AcceptedInvitationInfo(
    string OrgDisplayName,
    UserDisplayInfo InvitedBy,
    DateTimeOffset InvitedAt,
    DateTimeOffset AcceptedAt);
