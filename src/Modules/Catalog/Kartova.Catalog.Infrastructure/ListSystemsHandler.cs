using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;
using DomainSystem = Kartova.Catalog.Domain.CatalogSystem;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="ListSystemsQuery"/>. RLS scopes the result set; keyset
/// pagination via ToCursorPagedAsync (ADR-0095). Each page row is enriched with the creator
/// display name in one batched IUserDirectory round trip (mirrors ListApisHandler).</summary>
public sealed class ListSystemsHandler(IUserDirectory directory)
{
    private static readonly Func<DomainSystem, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<SystemResponse>> Handle(ListSystemsQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = SystemSortSpecs.Resolve(q.SortBy);

        // Apply filters BEFORE pagination so a hidden row never becomes a cursor boundary
        // (same invariant as ListApisHandler / ListServicesHandler).
        IQueryable<DomainSystem> source = db.Systems;

        if (q.TeamId.Length > 0)
            source = source.Where(s => q.TeamId.Contains(s.TeamId));   // = ANY(@p)

        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(s => EF.Functions.ILike(s.DisplayName, pattern, "\\"));
        }

        // Only non-empty dimensions are encoded so the cursor stays canonical and a
        // mid-pagination change trips CursorFilterMismatchException.
        Dictionary<string, string>? filters = null;
        if (q.TeamId.Length > 0 || q.DisplayNameContains is not null)
        {
            filters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (q.TeamId.Length > 0)
                filters["teamId"] = string.Join(",", q.TeamId.Select(g => g.ToString("D")).Order());
            if (q.DisplayNameContains is { } dn)
                filters["displayNameContains"] = dn;
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                SystemSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        var creatorIds = new HashSet<Guid>(page.Items.Select(s => s.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var items = page.Items
            .Select(s =>
            {
                var resp = s.ToResponse();
                return creators.TryGetValue(s.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
        return new CursorPage<SystemResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
