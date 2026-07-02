using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="SetApplicationSuccessorCommand"/>.
/// Returns <c>null</c> when no row is visible in the current tenant scope
/// (RLS auto-filters cross-tenant rows). No <c>If-Match</c>/<c>TimeProvider</c>
/// dependency — like the other lifecycle handlers, the domain invariant
/// ("current state must be Deprecated") serializes concurrent calls rather
/// than optimistic locking.
/// </summary>
public sealed class SetApplicationSuccessorHandler
{
    public async Task<ApplicationResponse?> Handle(
        SetApplicationSuccessorCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        var from = app.SuccessorApplicationId;
        app.SetSuccessor(cmd.SuccessorApplicationId);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(CatalogAuditEntries.SuccessorChanged(app, from), ct);
        return app.ToResponse();
    }
}
