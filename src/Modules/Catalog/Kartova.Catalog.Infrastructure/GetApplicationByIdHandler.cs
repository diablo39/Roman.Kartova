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
        // The Application entity stores its primary key in the private _id backing field
        // (plain Guid). Use EF.Property to access it in LINQ — this translates to
        // WHERE id = ? on PostgreSQL without any value-converter indirection.
        var app = await db.Applications.FirstOrDefaultAsync(
            x => EF.Property<Guid>(x, "_id") == q.Id, ct);
        return app?.ToResponse();
    }
}
