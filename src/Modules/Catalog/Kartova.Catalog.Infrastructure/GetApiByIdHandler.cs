using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetApiByIdQuery"/>. Returns null when the row is
/// invisible in the current tenant scope (RLS auto-filters). Enriches <c>CreatedBy</c>
/// via <see cref="IUserDirectory"/> (mirrors GetServiceByIdHandler).</summary>
public sealed class GetApiByIdHandler(IUserDirectory directory)
{
    public async Task<ApiResponse?> Handle(GetApiByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var api = await db.Apis.FirstOrDefaultAsync(ApiSortSpecs.IdEquals(q.Id), ct);
        if (api is null) return null;

        var creator = await directory.GetAsync(api.CreatedByUserId, ct);
        return api.ToResponse() with { CreatedBy = creator };
    }
}
