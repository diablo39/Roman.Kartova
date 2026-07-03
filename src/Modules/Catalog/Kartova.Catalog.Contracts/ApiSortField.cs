namespace Kartova.Catalog.Contracts;

/// <summary>Public sort-field allowlist for <c>GET /api/v1/catalog/apis</c> (ADR-0095).
/// Sortable on every displayed column (spec §3 #14).</summary>
public enum ApiSortField
{
    DisplayName,
    Style,
    Version,
    CreatedAt,
}
