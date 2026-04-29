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
        // ApplicationId is a strongly-typed value object with an EF Core value
        // converter. Comparing the wrapped value (x.Id == new ApplicationId(q.Id))
        // round-trips through the converter; comparing x.Id.Value directly does
        // not translate to SQL.
        var appId = new Kartova.Catalog.Domain.ApplicationId(q.Id);
        var app = await db.Applications.FirstOrDefaultAsync(x => x.Id == appId, ct);
        return app?.ToResponse();
    }
}
