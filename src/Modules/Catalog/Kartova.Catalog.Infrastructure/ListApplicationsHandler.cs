using System.Linq.Expressions;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;
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
    // The Application entity stores its primary key in the private _id backing field
    // (plain Guid) — EF.Property reads this directly without value converter indirection.
    // This is fully SQL-translatable (ORDER BY _id / WHERE _id > ?) on PostgreSQL.
    // The separate IdExtractor accesses the same value in-memory via the domain
    // property (x.Id.Value) for cursor encoding — EF.Property is not invokable
    // outside of an EF query context. See QueryablePagingExtensions for the
    // dual-expression overload that accommodates this split.
    private static readonly Expression<Func<DomainApplication, Guid>> IdSelectorExpr =
        x => EF.Property<Guid>(x, EfApplicationConfiguration.IdFieldName);

    private static readonly Func<DomainApplication, Guid> IdExtractor =
        x => x.Id.Value;

    public async Task<CursorPage<ApplicationResponse>> Handle(
        ListApplicationsQuery q,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var spec = ApplicationSortSpecs.Resolve(q.SortBy);

        var page = await db.Applications
            .ToCursorPagedAsync(spec, q.SortOrder, q.Cursor, q.Limit,
                IdSelectorExpr, IdExtractor, ct);

        var items = page.Items.Select(r => r.ToResponse()).ToList();
        return new CursorPage<ApplicationResponse>(items, page.NextCursor, PrevCursor: null);
    }
}
