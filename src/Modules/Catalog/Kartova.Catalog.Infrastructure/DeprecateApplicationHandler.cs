using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="DeprecateApplicationCommand"/>.
/// Returns <c>null</c> when no row is visible in the current tenant scope
/// (RLS auto-filters cross-tenant rows). Lifecycle endpoints take no
/// <c>If-Match</c> — the "current state must be Active" invariant inside
/// <see cref="Kartova.Catalog.Domain.Application.Deprecate"/> serializes
/// concurrent transitions.
/// </summary>
public sealed class DeprecateApplicationHandler
{
    private readonly TimeProvider _clock;

    public DeprecateApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse?> Handle(
        DeprecateApplicationCommand cmd,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        app.Deprecate(cmd.SunsetDate, _clock);
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
