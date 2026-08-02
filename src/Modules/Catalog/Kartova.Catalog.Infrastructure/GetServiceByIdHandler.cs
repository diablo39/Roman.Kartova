using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetServiceByIdQuery"/>. Returns null when the row
/// is invisible in the current tenant scope (RLS auto-filters). Enriches
/// <c>CreatedBy</c> via <see cref="IUserDirectory"/> (mirrors GetApplicationByIdHandler).
/// A2 (task 5c): also enriches the current System membership via
/// <see cref="ISystemMembershipEnricher"/>, so this detail read agrees with the list row.</summary>
public sealed class GetServiceByIdHandler(IUserDirectory directory, ISystemMembershipEnricher systemMembership)
{
    public async Task<ServiceResponse?> Handle(
        GetServiceByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var svc = await db.Services.FirstOrDefaultAsync(ServiceSortSpecs.IdEquals(q.Id), ct);
        if (svc is null) return null;

        var creator = await directory.GetAsync(svc.CreatedByUserId, ct);
        var systems = await systemMembership.SystemsForComponentsAsync(
            db, EntityKind.Service, [svc.Id.Value], ct);

        var resp = svc.ToResponse() with { CreatedBy = creator };
        if (systems.TryGetValue(svc.Id.Value, out var system))
            resp = resp with { SystemId = system.Id, SystemDisplayName = system.DisplayName };
        return resp;
    }
}
