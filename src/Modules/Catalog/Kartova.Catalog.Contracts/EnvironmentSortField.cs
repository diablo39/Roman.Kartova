namespace Kartova.Catalog.Contracts;

/// <summary>Sort allowlist for GET /catalog/environments (ADR-0095). Typed columns only.
/// Default = DisplayName asc.</summary>
public enum EnvironmentSortField
{
    DisplayName,
    CreatedAt,
    Type,
    Region,
}
