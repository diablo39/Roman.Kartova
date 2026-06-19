using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="UnDecommissionApplicationCommand"/>.
/// Returns <c>null</c> when no row is visible in the current tenant scope
/// (RLS auto-filters cross-tenant rows). Lifecycle endpoints take no
/// <c>If-Match</c> — the "current state must be Decommissioned" invariant
/// inside <see cref="Kartova.Catalog.Domain.Application.UnDecommission"/>
/// converts an invalid concurrent transition into a 409 on the second writer,
/// but does not prevent lost-updates under READ COMMITTED isolation. If the
/// conflict rate proves material, a future slice may add <c>SELECT FOR UPDATE</c>
/// or a concurrency token.
/// </summary>
public sealed class UnDecommissionApplicationHandler
{
    private readonly TimeProvider _clock;

    public UnDecommissionApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse?> Handle(
        UnDecommissionApplicationCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        var from = app.Lifecycle;
        app.UnDecommission(cmd.SunsetDate, _clock);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(CatalogAuditEntries.LifecycleChanged(app, from), ct);
        return app.ToResponse();
    }
}
