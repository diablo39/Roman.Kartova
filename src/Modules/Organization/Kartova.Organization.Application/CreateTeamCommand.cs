namespace Kartova.Organization.Application;

public sealed record CreateTeamCommand(string DisplayName, string? Description);
