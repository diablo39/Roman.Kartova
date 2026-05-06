using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="DeprecateApplicationCommand"/>. Mirrors
/// the <see cref="GetApplicationByIdHandler"/> nullable-return pattern: null = not
/// found in current tenant scope (RLS auto-filters cross-tenant rows). Endpoint
/// delegate maps null to RFC 7807 404.
///
/// <para>
/// Unlike <see cref="EditApplicationHandler"/>, this handler has no
/// concurrency-capture try/catch: lifecycle endpoints don't take <c>If-Match</c>
/// (slice 5 spec §3 Decision #7), so there's no optimistic-concurrency contract
/// to honor. The "current state must be Active" invariant inside
/// <see cref="Kartova.Catalog.Domain.Application.Deprecate"/> serializes
/// concurrent transitions; non-Active sources surface as
/// <c>InvalidLifecycleTransitionException</c>, mapped to 409 by
/// <c>LifecycleConflictExceptionHandler</c>. A past <c>sunsetDate</c> surfaces
/// as <see cref="ArgumentException"/>, mapped to 400 by
/// <c>DomainValidationExceptionHandler</c>.
/// </para>
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
