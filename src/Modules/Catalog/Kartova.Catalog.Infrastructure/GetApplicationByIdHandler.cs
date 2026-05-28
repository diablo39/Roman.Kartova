using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="GetApplicationByIdQuery"/>. Lives in Infrastructure
/// (mirrors <see cref="RegisterApplicationHandler"/>) because it depends on
/// <see cref="CatalogDbContext"/>. Returns null when the row is invisible in
/// the current tenant scope — RLS auto-filters cross-tenant rows.
/// <para>
/// Slice 9 / E1 (ADR-0098): the response is enriched with the owner's display
/// name via the <see cref="IUserDirectory"/> cross-module port. When the owning
/// user has been deleted from the directory (no matching <c>users</c> row),
/// <c>Owner</c> is left null — the wire contract treats the field as optional.
/// </para>
/// </summary>
public sealed class GetApplicationByIdHandler(IUserDirectory directory)
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
        if (app is null) return null;

        var owner = await directory.GetAsync(app.OwnerUserId, ct);
        return app.ToResponse() with { Owner = owner };
    }
}
