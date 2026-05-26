using Kartova.Organization.Domain;

namespace Kartova.Organization.Application;

public sealed record AddTeamMemberCommand(Guid TeamId, Guid UserId, TeamRole Role);

public sealed record AddTeamMemberResult(bool Added, bool TeamNotFound, bool AlreadyMember);
