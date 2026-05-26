namespace Kartova.Organization.Contracts;

/// <summary>
/// Public sort-field allowlist for <c>GET /api/v1/organizations/teams</c>.
/// Surfaces in OpenAPI as <c>SortByTeams</c>. ADR-0095.
/// </summary>
public enum TeamSortField
{
    CreatedAt,
    DisplayName
}
