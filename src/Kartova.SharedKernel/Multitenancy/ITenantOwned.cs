namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Marker for entities that are tenant-scoped. Architecture tests verify
/// every such entity has an RLS policy in a migration.
/// </summary>
public interface ITenantOwned
{
    TenantId TenantId { get; }
}
