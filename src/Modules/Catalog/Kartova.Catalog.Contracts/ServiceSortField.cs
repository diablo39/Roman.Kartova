namespace Kartova.Catalog.Contracts;

/// <summary>Public sort-field allowlist for <c>GET /api/v1/catalog/services</c>.
/// ADR-0095.</summary>
public enum ServiceSortField
{
    CreatedAt,
    DisplayName,
}
