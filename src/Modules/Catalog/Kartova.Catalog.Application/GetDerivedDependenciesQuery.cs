namespace Kartova.Catalog.Application;

/// <summary>Read a service's derived depends-on relationships (dependencies + dependents). Service-only (ADR-0111 §5).</summary>
public sealed record GetDerivedDependenciesQuery(Guid ServiceId);
