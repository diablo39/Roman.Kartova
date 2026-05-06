using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="GetApplicationByIdQuery"/>. Lives in Infrastructure
/// (mirrors <see cref="RegisterApplicationHandler"/>) because it depends on
/// <see cref="CatalogDbContext"/>. Returns null when the row is invisible in
/// the current tenant scope — RLS auto-filters cross-tenant rows.
/// </summary>
public sealed class GetApplicationByIdHandler
{
    public async Task<ApplicationResponse?> Handle(
        GetApplicationByIdQuery q,
        CatalogDbContext db,
        CancellationToken ct)
    {
        // Use ApplicationSortSpecs.IdEquals so this handler never references
        // EfApplicationConfiguration.IdFieldName (the EF backing-field string)
        // directly — the canonical reference lives in ApplicationSortSpecs alone.
        var app = await db.Applications.FirstOrDefaultAsync(
            ApplicationSortSpecs.IdEquals(q.Id), ct);
        return app?.ToResponse();
    }
}
