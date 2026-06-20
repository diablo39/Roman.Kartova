using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using DomainService = Kartova.Catalog.Domain.Service;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="ListServicesQuery"/>. RLS scopes the result set;
/// keyset pagination via ToCursorPagedAsync (ADR-0095). Each page row is enriched with
/// the creator display name in one batched IUserDirectory round trip (mirrors
/// ListApplicationsHandler).</summary>
public sealed class ListServicesHandler(IUserDirectory directory)
{
    private static readonly Func<DomainService, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<ServiceResponse>> Handle(
        ListServicesQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = ServiceSortSpecs.Resolve(q.SortBy);

        var page = await db.Services
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ServiceSortSpecs.IdSelector, IdExtractor, ct);

        var creatorIds = new HashSet<Guid>(page.Items.Select(s => s.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var items = page.Items
            .Select(r =>
            {
                var resp = r.ToResponse();
                return creators.TryGetValue(r.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
        return new CursorPage<ServiceResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
