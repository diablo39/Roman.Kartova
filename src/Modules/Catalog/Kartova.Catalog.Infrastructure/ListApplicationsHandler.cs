using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="ListApplicationsQuery"/>. Lives in Infrastructure
/// (mirrors <see cref="GetApplicationByIdHandler"/>) because it depends on
/// <see cref="CatalogDbContext"/>. RLS auto-filters cross-tenant rows so the
/// result set is implicitly scoped to the current tenant (ADR-0090).
/// </summary>
public sealed class ListApplicationsHandler
{
    public async Task<IReadOnlyList<ApplicationResponse>> Handle(
        ListApplicationsQuery _,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var rows = await db.Applications
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(r => r.ToResponse()).ToList();
    }
}
