namespace Kartova.Organization.Application;

public sealed record RemoveTeamMemberCommand(Guid TeamId, Guid UserId);

/// <summary>
/// Mutually-exclusive terminal outcomes of a remove-team-member command. An enum, not a
/// boolean-flag record, per ADR-0104 (payload-free outcome → enum).
/// </summary>
public enum RemoveTeamMemberOutcome
{
    Removed,
    TeamNotFound,
    MemberNotFound,
}
