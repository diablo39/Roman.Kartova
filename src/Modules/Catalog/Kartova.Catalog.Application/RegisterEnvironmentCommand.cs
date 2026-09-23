using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Register a new tenant-global <see cref="CatalogEnvironment"/>. Tenant id +
/// created-by come from request context (ADR-0090). ResourceDetailsJson is the already-validated
/// (<see cref="EnvironmentResourceDetails.ToJson"/>) opaque jsonb payload.</summary>
public sealed record RegisterEnvironmentCommand(
    string DisplayName,
    string Description,
    EnvironmentType Type,
    string? Region,
    string ResourceDetailsJson);
