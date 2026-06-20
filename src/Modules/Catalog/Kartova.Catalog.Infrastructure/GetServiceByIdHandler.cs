using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetServiceByIdQuery"/>. Returns null when the row
/// is invisible in the current tenant scope (RLS auto-filters). Enriches
/// <c>CreatedBy</c> via <see cref="IUserDirectory"/> (mirrors GetApplicationByIdHandler).</summary>
public sealed class GetServiceByIdHandler(IUserDirectory directory)
{
    public async Task<ServiceResponse?> Handle(
        GetServiceByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var svc = await db.Services.FirstOrDefaultAsync(ServiceSortSpecs.IdEquals(q.Id), ct);
        if (svc is null) return null;

        var creator = await directory.GetAsync(svc.CreatedByUserId, ct);
        return svc.ToResponse() with { CreatedBy = creator };
    }
}
