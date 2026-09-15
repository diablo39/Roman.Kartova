namespace Kartova.Catalog.Domain;

/// <summary>
/// Restricts <see cref="RelationshipType.DeployedOn"/> to virtual-machine infrastructure
/// targets (Task 3, catalog-vm-linking). <see cref="RelationshipTypeRules.IsAllowedPair"/>
/// only sees <see cref="EntityKind"/>, not <see cref="InfrastructureType"/>, so this
/// finer-grained check is enforced separately at the create path
/// (<c>CatalogEndpointDelegates.CreateRelationshipAsync</c>).
/// </summary>
public static class DeployedOnTargetRules
{
    public static bool IsAllowedInfrastructureType(InfrastructureType type)
        => type == InfrastructureType.VirtualMachine;
}
