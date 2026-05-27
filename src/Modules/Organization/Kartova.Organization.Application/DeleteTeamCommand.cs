namespace Kartova.Organization.Application;

public sealed record DeleteTeamCommand(Guid Id);

public sealed record DeleteTeamResult(bool Deleted, bool NotFound, int? ApplicationsAssigned);
