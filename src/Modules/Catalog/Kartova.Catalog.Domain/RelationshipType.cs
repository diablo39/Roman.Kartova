namespace Kartova.Catalog.Domain;

public enum RelationshipType
{
    DependsOn,
    ProvidesApiFor,
    ConsumesApiFrom,
    PublishesTo,
    SubscribesFrom,
    DeployedOn,
    InstanceOf,
    PartOf,
}
