namespace Kartova.Catalog.Domain;

public static class RelationshipTypeRules
{
    /// <summary>Component-kind sources for the at-most-one-System PartOf invariant: any kind that
    /// can hold a PartOf edge into a System. Shared by the pre-check and the 23505 catch `when`
    /// filter in CatalogEndpointDelegates.CreateRelationshipAsync so the two scopings cannot drift
    /// apart again (final-review fix, catalog-vm-linking: Infrastructure was added to
    /// IsAllowedPair's PartOf case below but the two call sites still checked
    /// Application/Service only, so a duplicate infra PartOf POST fell through to an unhandled
    /// DbUpdateException / HTTP 500 instead of the 409 the other kinds return).</summary>
    public static bool IsPartOfSourceKind(EntityKind kind)
        => kind is EntityKind.Application or EntityKind.Service or EntityKind.Infrastructure;

    public static bool IsCreatable(RelationshipType type)
        => type is RelationshipType.DependsOn
            or RelationshipType.InstanceOf
            or RelationshipType.ProvidesApiFor
            or RelationshipType.ConsumesApiFrom
            or RelationshipType.PartOf
            or RelationshipType.DeployedOn;

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
        RelationshipType.PartOf => IsPartOfSourceKind(source) && target == EntityKind.System,
        // VM-only restriction is not expressible here (rules are kind-level, InfrastructureType
        // is not visible) — it is enforced at the create path (see Task 3).
        RelationshipType.DeployedOn =>
            source is EntityKind.Application or EntityKind.Service && target == EntityKind.Infrastructure,
        _ => false,
    };
}
