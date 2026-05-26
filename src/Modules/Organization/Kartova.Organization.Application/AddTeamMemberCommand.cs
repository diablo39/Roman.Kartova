using Kartova.Organization.Domain;

namespace Kartova.Organization.Application;

public sealed record AddTeamMemberCommand(Guid TeamId, Guid UserId, TeamRole Role);

/// <summary>
/// Result of <see cref="AddTeamMemberCommand"/>. <see cref="AddedAt"/> carries the
/// canonical timestamp the handler persisted (so the endpoint surfaces the exact
/// value the DB sees, not a re-clocked wall-clock snapshot) on the happy path and
/// is <c>null</c> on the not-found / already-member branches.
/// </summary>
public sealed record AddTeamMemberResult(bool Added, bool TeamNotFound, bool AlreadyMember, DateTimeOffset? AddedAt);
