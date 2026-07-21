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
        RelationshipType.PartOf =>
            source is EntityKind.Application or EntityKind.Service && target == EntityKind.System,
        _ => false,
    };
}
