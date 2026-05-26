namespace Kartova.Organization.Application;

public sealed record RemoveTeamMemberCommand(Guid TeamId, Guid UserId);

public sealed record RemoveTeamMemberResult(bool Removed, bool TeamNotFound, bool MemberNotFound);
