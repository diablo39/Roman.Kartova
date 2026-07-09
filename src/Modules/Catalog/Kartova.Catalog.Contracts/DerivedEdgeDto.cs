using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>A derived service→service depends-on edge (never persisted). Type is implicitly depends-on
/// (the only derived edge kind), so — unlike <see cref="GraphEdgeDto"/> — it carries no Type/Origin.
/// <see cref="Paths"/> lists every API (and optional via-app) linking source→target.</summary>
[ExcludeFromCodeCoverage]
public sealed record DerivedEdgeDto(
    GraphEndpointDto Source, GraphEndpointDto Target, IReadOnlyList<DerivationPathDto> Paths);
