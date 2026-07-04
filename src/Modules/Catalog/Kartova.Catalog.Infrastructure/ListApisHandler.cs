using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using DomainApi = Kartova.Catalog.Domain.Api;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="ListApisQuery"/>. RLS scopes the result set; keyset
/// pagination via ToCursorPagedAsync (ADR-0095). Each page row is enriched with the creator
/// display name in one batched IUserDirectory round trip (mirrors ListServicesHandler).
/// No attribute filters this slice (FU-9).</summary>
public sealed class ListApisHandler(IUserDirectory directory)
{
    private static readonly Func<DomainApi, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<ApiResponse>> Handle(ListApisQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = ApiSortSpecs.Resolve(q.SortBy);

        var page = await db.Apis
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApiSortSpecs.IdSelector, IdExtractor, ct);

        var creatorIds = new HashSet<Guid>(page.Items.Select(a => a.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var items = page.Items
            .Select(a =>
            {
                var resp = a.ToResponse();
                return creators.TryGetValue(a.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
        return new CursorPage<ApiResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
