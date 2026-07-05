namespace Kartova.Catalog.Domain;

public static class RelationshipTypeRules
{
    public static bool IsCreatable(RelationshipType type)
        => type is RelationshipType.DependsOn
            or RelationshipType.InstanceOf
            or RelationshipType.ProvidesApiFor
            or RelationshipType.ConsumesApiFrom;

    public static bool IsAllowedPair(RelationshipType type, EntityKind source, EntityKind target) => type switch
    {
        RelationshipType.DependsOn => true,
        RelationshipType.InstanceOf => source == EntityKind.Service && target == EntityKind.Application,
        RelationshipType.ProvidesApiFor => source is EntityKind.Application or EntityKind.Service && target == EntityKind.Api,
        RelationshipType.ConsumesApiFrom => source is EntityKind.Application or EntityKind.Service && target == EntityKind.Api,
        _ => false,
    };
}
