using Kartova.Organization.Domain;

namespace Kartova.Organization.Application;

public sealed record UpdateTeamMemberCommand(Guid TeamId, Guid UserId, TeamRole NewRole);

public sealed record UpdateTeamMemberResult(bool Updated, bool TeamNotFound, bool MemberNotFound);
