using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Read an entity's API surface (provides direct+derived, consumes direct). Kind is Service or Application.</summary>
public sealed record GetApiSurfaceQuery(EntityKind Kind, Guid EntityId);
