using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public interface ICatalogEntityLookup
{
    Task<EntityLookupResult?> Find(EntityKind kind, Guid id, CancellationToken ct);
}

/// <param name="TeamId">The owning team of the looked-up resource.</param>
/// <param name="DisplayName">The looked-up resource's display name.</param>
/// <param name="Type">
/// The resource's <see cref="InfrastructureType"/> — non-null ONLY when the lookup was for
/// <see cref="EntityKind.Infrastructure"/>. Null for every other <see cref="EntityKind"/>.
/// Consumers MUST treat null as "not applicable / not looked up" and MUST NOT default it to a
/// concrete type — a null defaulted to <c>default(InfrastructureType)</c> would silently mean
/// <see cref="InfrastructureType.VirtualMachine"/>.
/// </param>
public sealed record EntityLookupResult(Guid TeamId, string DisplayName, InfrastructureType? Type = null);
