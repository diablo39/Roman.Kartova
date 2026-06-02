using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UserTeamMembership(Guid TeamId, string TeamName, string Role);
