namespace Kartova.Catalog.Contracts;

/// <summary>Public sort-field allowlist for <c>GET /api/v1/catalog/infrastructure</c>.
/// Slice-1 allowlist — JSONB attribute sort is deferred. ADR-0095.
/// <c>Provider</c> is a typed column (added slice 2a) — keyset-safe like <c>Type</c>.</summary>
public enum InfrastructureSortField
{
    CreatedAt,
    DisplayName,
    Type,
    Provider,
}
