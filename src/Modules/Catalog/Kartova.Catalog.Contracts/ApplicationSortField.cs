namespace Kartova.Catalog.Contracts;

/// <summary>
/// Public sort-field allowlist for <c>GET /api/v1/catalog/applications</c>.
/// Surfaces in OpenAPI as <c>SortByApplications</c>. ADR-0095.
/// </summary>
public enum ApplicationSortField
{
    CreatedAt,
    Name
}
