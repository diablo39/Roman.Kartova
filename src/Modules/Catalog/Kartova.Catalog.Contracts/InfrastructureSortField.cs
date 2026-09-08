namespace Kartova.Catalog.Contracts;

/// <summary>Public sort-field allowlist for <c>GET /api/v1/catalog/infrastructure</c>.
/// Slice-1 allowlist — JSONB attribute sort is deferred. ADR-0095.</summary>
public enum InfrastructureSortField
{
    CreatedAt,
    DisplayName,
    Type,
}
