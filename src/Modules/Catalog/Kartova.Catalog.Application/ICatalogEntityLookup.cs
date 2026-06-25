using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public interface ICatalogEntityLookup
{
    Task<EntityLookupResult?> Find(EntityKind kind, Guid id, CancellationToken ct);
}

public sealed record EntityLookupResult(Guid TeamId, string DisplayName);
