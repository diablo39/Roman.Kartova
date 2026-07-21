namespace Kartova.Catalog.Contracts;

/// <summary>Public sort-field allowlist for <c>GET /api/v1/catalog/systems</c> (ADR-0095).
/// Default sort is <c>displayName asc</c> (design §5).</summary>
public enum SystemSortField
{
    DisplayName,
    CreatedAt,
}
