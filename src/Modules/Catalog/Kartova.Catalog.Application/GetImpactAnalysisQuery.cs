using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Read a Service's or Application's blast radius (transitive dependents over explicit ∪
/// derived depends-on). Subject kind is Service or Application only (validated at the endpoint).</summary>
public sealed record GetImpactAnalysisQuery(EntityKind FocusKind, Guid FocusId);
