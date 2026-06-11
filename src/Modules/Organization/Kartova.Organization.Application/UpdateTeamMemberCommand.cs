using Kartova.Organization.Domain;

namespace Kartova.Organization.Application;

public sealed record UpdateTeamMemberCommand(Guid TeamId, Guid UserId, TeamRole NewRole);

/// <summary>
/// Mutually-exclusive terminal outcomes of an update-team-member command. An enum, not a
/// boolean-flag record, per ADR-0104 (payload-free outcome → enum).
/// </summary>
public enum UpdateTeamMemberOutcome
{
    Updated,
    TeamNotFound,
    MemberNotFound,
}
