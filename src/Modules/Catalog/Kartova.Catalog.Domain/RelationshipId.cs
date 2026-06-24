namespace Kartova.Catalog.Domain;

public readonly record struct RelationshipId(Guid Value)
{
    public static RelationshipId New() => new(Guid.NewGuid());
}
