using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class CatalogEntityLookup(CatalogDbContext db) : ICatalogEntityLookup
{
    public async Task<EntityLookupResult?> Find(EntityKind kind, Guid id, CancellationToken ct) => kind switch
    {
        EntityKind.Application => await db.Applications
            .Where(a => EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName) == id)
            .Select(a => new EntityLookupResult(a.TeamId, a.DisplayName))
            .SingleOrDefaultAsync(ct),
        EntityKind.Service => await db.Services
            .Where(s => EF.Property<Guid>(s, EfServiceConfiguration.IdFieldName) == id)
            .Select(s => new EntityLookupResult(s.TeamId, s.DisplayName))
            .SingleOrDefaultAsync(ct),
        EntityKind.Api => await db.Apis
            .Where(a => EF.Property<Guid>(a, EfApiConfiguration.IdFieldName) == id)
            .Select(a => new EntityLookupResult(a.TeamId, a.DisplayName))
            .SingleOrDefaultAsync(ct),
        EntityKind.System => await db.Systems
            .Where(x => EF.Property<Guid>(x, EfSystemConfiguration.IdFieldName) == id)
            .Select(x => new EntityLookupResult(x.TeamId, x.DisplayName))
            .SingleOrDefaultAsync(ct),
        _ => null,
    };
}
