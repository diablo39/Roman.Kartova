using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="GetApplicationByIdQuery"/>. Lives in Infrastructure
/// (mirrors <see cref="RegisterApplicationHandler"/>) because it depends on
/// <see cref="CatalogDbContext"/>. Returns null when the row is invisible in
/// the current tenant scope — RLS auto-filters cross-tenant rows.
/// <para>
/// Slice 9 / E1 (ADR-0098): the response is enriched with the creator's display
/// name via the <see cref="IUserDirectory"/> cross-module port. When the creating
/// user has been deleted from the directory (no matching <c>users</c> row),
/// <c>CreatedBy</c> is left null — the wire contract treats the field as optional.
/// </para>
/// <para>
/// A2 (task 5c): also enriched with the current System membership, via the same
/// <see cref="ISystemMembershipEnricher"/> port the list handlers use — so this detail
/// read agrees with the list row for the same component instead of always reporting
/// <c>systemId: null</c>.
/// </para>
/// </summary>
public sealed class GetApplicationByIdHandler(IUserDirectory directory, ISystemMembershipEnricher systemMembership)
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

        var creator = await directory.GetAsync(app.CreatedByUserId, ct);

        string? successorDisplayName = null;
        if (app.SuccessorApplicationId is { } successorId)
        {
            successorDisplayName = await db.Applications
                .Where(ApplicationSortSpecs.IdEquals(successorId))
                .Select(a => a.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        var systems = await systemMembership.SystemsForComponentsAsync(
            db, EntityKind.Application, [app.Id.Value], ct);

        var resp = app.ToResponse() with { CreatedBy = creator, SuccessorDisplayName = successorDisplayName };
        if (systems.TryGetValue(app.Id.Value, out var system))
            resp = resp with { SystemId = system.Id, SystemDisplayName = system.DisplayName };
        return resp;
    }
}
