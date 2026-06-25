namespace Kartova.Catalog.Domain;

public static class RelationshipTypeRules
{
    public static bool IsCreatable(RelationshipType type)
        => type is RelationshipType.DependsOn or RelationshipType.PartOf;

    public static bool IsAllowedPair(RelationshipType type, EntityKind source, EntityKind target) => type switch
    {
        RelationshipType.DependsOn => true,
        RelationshipType.PartOf => source == EntityKind.Service && target == EntityKind.Application,
        _ => false,
    };
}
