using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="ListApplicationsQuery"/>. RLS auto-filters cross-tenant
/// rows so the result set is implicitly scoped to the current tenant (ADR-0090).
/// Pagination applied via <see cref="QueryablePagingExtensions.ToCursorPagedAsync{T}"/>
/// (ADR-0095).
/// </summary>
public sealed class ListApplicationsHandler
{
    public async Task<CursorPage<ApplicationResponse>> Handle(
        ListApplicationsQuery q,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var spec = ApplicationSortSpecs.Resolve(q.SortBy);

        var page = await db.Applications
            .ToCursorPagedAsync(spec, q.SortOrder, q.Cursor, q.Limit, x => x.Id.Value, ct);

        var items = page.Items.Select(r => r.ToResponse()).ToList();
        return new CursorPage<ApplicationResponse>(items, page.NextCursor, PrevCursor: null);
    }
}
