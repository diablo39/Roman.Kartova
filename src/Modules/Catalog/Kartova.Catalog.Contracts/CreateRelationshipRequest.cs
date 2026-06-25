using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record CreateRelationshipRequest(
    EntityKind SourceKind, Guid SourceId, RelationshipType Type, EntityKind TargetKind, Guid TargetId);
