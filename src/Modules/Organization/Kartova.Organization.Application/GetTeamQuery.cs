namespace Kartova.Organization.Application;

/// <summary>
/// Query for a single team's detail view (members + assigned application ids).
/// RLS scopes the result to the current tenant (ADR-0090).
/// </summary>
public sealed record GetTeamQuery(Guid Id);
