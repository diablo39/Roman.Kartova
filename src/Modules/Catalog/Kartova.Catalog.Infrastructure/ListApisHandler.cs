using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;
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

        // Apply filters BEFORE pagination so a hidden row never becomes a cursor boundary
        // (same invariant as ListServicesHandler).
        IQueryable<DomainApi> source = db.Apis;

        if (q.TeamId.Length > 0)
            source = source.Where(a => q.TeamId.Contains(a.TeamId));   // = ANY(@p)

        if (q.Style.Length > 0)
            source = source.Where(a => q.Style.Contains(a.Style));     // = ANY(@p)

        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(a => EF.Functions.ILike(a.DisplayName, pattern, "\\"));
        }

        // Only non-empty dimensions are encoded so the cursor stays canonical and a
        // mid-pagination change trips CursorFilterMismatchException.
        Dictionary<string, string>? filters = null;
        if (q.TeamId.Length > 0 || q.Style.Length > 0 || q.DisplayNameContains is not null)
        {
            filters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (q.TeamId.Length > 0)
                filters["teamId"] = string.Join(",", q.TeamId.Select(g => g.ToString("D")).Order());
            if (q.Style.Length > 0)
                filters["style"] = string.Join(",", q.Style.Select(s => s.ToString()).Order());
            if (q.DisplayNameContains is { } dn)
                filters["displayNameContains"] = dn;
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApiSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        var creatorIds = new HashSet<Guid>(page.Items.Select(a => a.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var pageApiIds = page.Items.Select(a => a.Id).ToList();
        var idsWithSpec = (await db.ApiSpecs
            .Where(s => pageApiIds.Contains(s.ApiId))
            .Select(s => s.ApiId)
            .ToListAsync(ct))
            .Select(id => id.Value)
            .ToHashSet();

        var items = page.Items
            .Select(a =>
            {
                var resp = a.ToResponse() with { HasSpec = idsWithSpec.Contains(a.Id.Value) };
                return creators.TryGetValue(a.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
        return new CursorPage<ApiResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
