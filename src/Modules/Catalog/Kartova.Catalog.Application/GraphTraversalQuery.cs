using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record GraphTraversalQuery(EntityRef Focus, int Depth, RelationshipDirection Direction);
