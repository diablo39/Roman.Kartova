using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetSystemByIdQuery"/>. Returns null when the row is
/// invisible in the current tenant scope (RLS auto-filters). Enriches <c>CreatedBy</c>
/// via <see cref="IUserDirectory"/> (mirrors GetApiByIdHandler).</summary>
public sealed class GetSystemByIdHandler(IUserDirectory directory)
{
    public async Task<SystemResponse?> Handle(GetSystemByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var system = await db.Systems.FirstOrDefaultAsync(SystemSortSpecs.IdEquals(q.Id), ct);
        if (system is null) return null;

        var creator = await directory.GetAsync(system.CreatedByUserId, ct);
        return system.ToResponse() with { CreatedBy = creator };
    }
}
