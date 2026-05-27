namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Lightweight membership projection used by authorization filters; carries the
/// team identity and the caller's role on that team.
/// </summary>
public sealed record TeamMembershipInfo(Guid TeamId, TeamRoleKind Role);
