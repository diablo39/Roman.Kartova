namespace Kartova.Catalog.Domain;

/// <summary>Deployment-environment tier (E-02.F-05, ADR-0117). One unified
/// <see cref="CatalogEnvironment"/> aggregate keyed by this value. Persisted as
/// smallint — append at end only; the numeric values must stay stable.</summary>
public enum EnvironmentType
{
    Development = 0,
    Staging = 1,
    Production = 2,
}
