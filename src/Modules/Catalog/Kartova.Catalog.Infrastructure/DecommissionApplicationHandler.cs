using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="DecommissionApplicationCommand"/>.
/// Returns <c>null</c> when no row is visible in the current tenant scope
/// (RLS auto-filters cross-tenant rows). Lifecycle endpoints take no
/// <c>If-Match</c> — domain invariants ("current state must be Deprecated"
/// and "now &gt;= sunsetDate") serialize concurrent transitions.
/// </summary>
public sealed class DecommissionApplicationHandler
{
    private readonly TimeProvider _clock;

    public DecommissionApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse?> Handle(
        DecommissionApplicationCommand cmd,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        app.Decommission(_clock);
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
