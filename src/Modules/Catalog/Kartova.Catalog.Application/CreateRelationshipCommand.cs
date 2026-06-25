using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record CreateRelationshipCommand(EntityRef Source, EntityRef Target, RelationshipType Type);
