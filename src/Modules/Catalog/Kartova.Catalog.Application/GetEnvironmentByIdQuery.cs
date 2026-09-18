namespace Kartova.Catalog.Application;

/// <summary>Fetch one environment by id within the current tenant scope (RLS-filtered).
/// Resolves to <c>null</c> when absent or cross-tenant.</summary>
public sealed record GetEnvironmentByIdQuery(Guid Id);
