using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="DecommissionApplicationCommand"/>. Mirrors the
/// <see cref="DeprecateApplicationHandler"/> nullable-return pattern: null = not found in
/// current tenant scope (RLS auto-filters cross-tenant rows). Endpoint delegate maps null
/// to RFC 7807 404.
///
/// <para>
/// Like <see cref="DeprecateApplicationHandler"/>, no concurrency-capture try/catch:
/// lifecycle endpoints don't take <c>If-Match</c> (slice 5 spec §3 Decision #7). The
/// "current state must be Deprecated" invariant inside
/// <see cref="Kartova.Catalog.Domain.Application.Decommission"/> serializes concurrent
/// transitions; non-Deprecated sources surface as <c>InvalidLifecycleTransitionException</c>,
/// and so does a "now &lt; sunsetDate" attempt (with <c>reason="before-sunset-date"</c>
/// and the stored <c>sunsetDate</c> attached). Both are mapped to 409 by
/// <c>LifecycleConflictExceptionHandler</c>.
/// </para>
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
