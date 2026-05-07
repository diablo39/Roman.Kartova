using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="ListApplicationsQuery"/>. RLS auto-filters cross-tenant
/// rows so the result set is implicitly scoped to the current tenant (ADR-0090).
/// Pagination applied via <see cref="QueryablePagingExtensions.ToCursorPagedAsync{T}"/>
/// (ADR-0095).
/// </summary>
public sealed class ListApplicationsHandler
{
    // The separate IdExtractor accesses the primary key in-memory via the domain
    // property (x.Id.Value) for cursor encoding — EF.Property is not invokable
    // outside of an EF query context. ApplicationSortSpecs.IdSelector provides
    // the EF-translatable expression. See QueryablePagingExtensions for the
    // dual-expression overload that accommodates this split.
    private static readonly Func<DomainApplication, Guid> IdExtractor =
        x => x.Id.Value;

    public async Task<CursorPage<ApplicationResponse>> Handle(
        ListApplicationsQuery q,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var spec = ApplicationSortSpecs.Resolve(q.SortBy);

        // Apply ADR-0073 default-view filter before pagination so the keyset
        // bounds stay consistent: a row that's hidden by the filter must never
        // appear as a cursor boundary, otherwise the next page would silently
        // skip rows. The cursor JSON (CursorCodec.ic) is mismatch-checked inside
        // ToCursorPagedAsync.
        IQueryable<DomainApplication> source = db.Applications;
        if (!q.IncludeDecommissioned)
        {
            source = source.Where(a => a.Lifecycle != Lifecycle.Decommissioned);
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApplicationSortSpecs.IdSelector, IdExtractor, ct,
                expectedIncludeDecommissioned: q.IncludeDecommissioned);

        var items = page.Items.Select(r => r.ToResponse()).ToList();
        return new CursorPage<ApplicationResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
