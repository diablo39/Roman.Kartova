using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="ReactivateApplicationCommand"/>.
/// Returns <c>null</c> when no row is visible in the current tenant scope
/// (RLS auto-filters cross-tenant rows). Lifecycle endpoints take no
/// <c>If-Match</c> — the "current state must be Deprecated or Decommissioned"
/// invariant inside <see cref="Kartova.Catalog.Domain.Application.Reactivate"/>
/// serializes concurrent transitions.
/// </summary>
public sealed class ReactivateApplicationHandler
{
    public async Task<ApplicationResponse?> Handle(
        ReactivateApplicationCommand cmd,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        app.Reactivate();
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
