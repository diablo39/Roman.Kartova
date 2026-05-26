namespace Kartova.Organization.Application;

public sealed record UpdateTeamCommand(Guid Id, string DisplayName, string? Description);
