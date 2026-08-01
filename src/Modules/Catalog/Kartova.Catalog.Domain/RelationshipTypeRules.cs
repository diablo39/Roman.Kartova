namespace Kartova.Catalog.Domain;

public static class RelationshipTypeRules
{
    public static bool IsCreatable(RelationshipType type)
        => type is RelationshipType.DependsOn
            or RelationshipType.InstanceOf
            or RelationshipType.ProvidesApiFor
            or RelationshipType.ConsumesApiFrom
            or RelationshipType.PartOf;

    public static bool IsAllowedPair(RelationshipType type, EntityKind source, EntityKind target) => type switch
    {
        // A System is a grouping entity that participates only via PartOf — it must never
        // appear on either side of a DependsOn edge (a System itself has nothing to depend
        // on, and nothing depends "on a group" rather than on its members).
        RelationshipType.DependsOn => source != EntityKind.System && target != EntityKind.System,
        RelationshipType.InstanceOf => source == EntityKind.Service && target == EntityKind.Application,
        RelationshipType.ProvidesApiFor or RelationshipType.ConsumesApiFrom =>
            source is EntityKind.Application or EntityKind.Service && target == EntityKind.Api,
        // Structural source/target pair only — this does NOT encode at-most-one-System-per-
        // component. That invariant is enforced by the ux_relationships_one_system unique index
        // and decided application-side by Kartova.Catalog.Application.SystemMembership.Decide
        // before a write ever reaches here (see SetComponentSystemHandler and
        // CatalogEndpointDelegates.CreateRelationshipAsync, the two paths that call it). A fourth
        // PartOf write path that skips SystemMembership.Decide would only discover the at-most-one
        // rule from a runtime 23505.
        RelationshipType.PartOf =>
            source is EntityKind.Application or EntityKind.Service && target == EntityKind.System,
        _ => false,
    };
}
