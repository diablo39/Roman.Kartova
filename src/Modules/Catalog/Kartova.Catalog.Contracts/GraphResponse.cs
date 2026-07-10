using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record GraphResponse(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges,
    IReadOnlyList<DerivedEdgeDto> DerivedEdges,
    bool Truncated);

[ExcludeFromCodeCoverage]
public sealed record GraphNodeDto(
    EntityKind Kind, Guid Id, string DisplayName, int Depth, Guid? TeamId,
    int OutDegree, int InDegree);

[ExcludeFromCodeCoverage]
public sealed record GraphEdgeDto(
    Guid Id, GraphEndpointDto Source, GraphEndpointDto Target,
    RelationshipType Type, RelationshipOrigin Origin);

[ExcludeFromCodeCoverage]
public sealed record GraphEndpointDto(EntityKind Kind, Guid Id);
