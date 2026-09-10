using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="ListInfrastructureQuery"/>. Generic across every
/// <see cref="InfrastructureType"/> — shared columns only, NEVER deserializes the opaque
/// <c>Attributes</c> jsonb payload (ADR-0111 amendment). RLS scopes the result set; keyset
/// pagination via ToCursorPagedAsync (ADR-0095). Filters are applied BEFORE pagination so a
/// hidden row never becomes a cursor boundary (same invariant as ListServicesHandler).
/// </summary>
public sealed class ListInfrastructureHandler
{
    private static readonly Func<InfrastructureResource, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<InfrastructureListItemResponse>> Handle(
        ListInfrastructureQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = InfrastructureSortSpecs.Resolve(q.SortBy);
        IQueryable<InfrastructureResource> source = db.Infrastructure;

        // teamId filter: Array.Contains(column) → SQL = ANY(@p) via Npgsql.
        if (q.TeamId.Length > 0)
            source = source.Where(x => q.TeamId.Contains(x.TeamId));

        // type filter: Array.Contains(column) → SQL = ANY(@p) via Npgsql.
        if (q.Type is { Length: > 0 } types)
            source = source.Where(x => types.Contains(x.Type));

        // Build the f-map dict. Only non-empty filter dimensions are encoded so the cursor
        // stays canonical and a mid-pagination change trips CursorFilterMismatchException.
        Dictionary<string, string>? filters = null;
        if (q.TeamId.Length > 0 || q.Type is { Length: > 0 })
        {
            filters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (q.TeamId.Length > 0)
                filters["teamId"] = CursorFilterValues.Join(q.TeamId);
            if (q.Type is { Length: > 0 } t)
                filters["type"] = CursorFilterValues.Join(t.Select(x => x.ToString()));
        }

        var page = await source.ToCursorPagedAsync(
            spec, q.SortOrder, q.Cursor, q.Limit,
            InfrastructureSortSpecs.IdSelector, IdExtractor, ct, expectedFilters: filters);

        var items = page.Items.Select(ToListItem).ToList();
        return new CursorPage<InfrastructureListItemResponse>(items, page.NextCursor, page.PrevCursor);
    }

    private static InfrastructureListItemResponse ToListItem(InfrastructureResource x) =>
        new(x.Id.Value, x.TenantId.Value, x.DisplayName, x.Description,
            KindWire(x.Type), x.Provider, x.TeamId, x.SystemId, x.CreatedByUserId, x.CreatedAt);

    /// <summary>camelCase enum-name wire; extend as new Infrastructure types land.</summary>
    internal static string KindWire(InfrastructureType t) => t switch
    {
        InfrastructureType.VirtualMachine => "virtualMachine",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "unknown infrastructure type"),
    };
}
