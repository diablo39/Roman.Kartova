using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RelationshipResponse(
    Guid Id, EntityRefDto Source, EntityRefDto Target,
    RelationshipType Type, RelationshipOrigin Origin,
    Guid CreatedByUserId, DateTimeOffset CreatedAt);
