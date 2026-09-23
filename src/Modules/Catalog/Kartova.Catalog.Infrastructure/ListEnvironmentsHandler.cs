using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="ListEnvironmentsQuery"/>. Tenant-scoped via RLS. Type is a
/// multi-select equality-in filter over the typed column; Region is exact-match; DisplayNameContains
/// is a case-insensitive ILIKE substring (wildcards escaped). ADR-0095/0107.</summary>
public sealed class ListEnvironmentsHandler
{
    private static readonly Func<CatalogEnvironment, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<EnvironmentListItemResponse>> Handle(
        ListEnvironmentsQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = EnvironmentSortSpecs.Resolve(q.SortBy);
        IQueryable<CatalogEnvironment> source = db.Environments;

        if (q.Type is { Length: > 0 } types)
            source = source.Where(x => types.Contains(x.Type));
        if (q.Region is { } region)
            source = source.Where(x => x.Region == region);
        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(x => EF.Functions.ILike(x.DisplayName, pattern, "\\"));
        }

        var filters = BuildFilterMap(q);

        var page = await source.ToCursorPagedAsync(
            spec, q.SortOrder, q.Cursor, q.Limit,
            EnvironmentSortSpecs.IdSelector, IdExtractor, ct, expectedFilters: filters);

        var items = page.Items.Select(x => new EnvironmentListItemResponse(
            x.Id.Value, x.TenantId.Value, x.DisplayName, x.Description, x.Type, x.Region,
            x.CreatedByUserId, x.CreatedAt)).ToList();

        return new CursorPage<EnvironmentListItemResponse>(items, page.NextCursor, page.PrevCursor);
    }

    /// <summary>Cursor f-map — only non-empty dimensions, so a mid-pagination filter change trips
    /// CursorFilterMismatchException. Keys are camelCase wire names.</summary>
    internal static Dictionary<string, string>? BuildFilterMap(ListEnvironmentsQuery q)
    {
        if ((q.Type is null || q.Type.Length == 0) && q.Region is null && q.DisplayNameContains is null)
            return null;

        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q.Type is { Length: > 0 } types)
            filters["type"] = CursorFilterValues.Join(types.Select(t => t.ToString()));
        if (q.Region is { } region)
            filters["region"] = region;
        if (q.DisplayNameContains is { } name)
            filters["displayNameContains"] = name;
        return filters;
    }
}
