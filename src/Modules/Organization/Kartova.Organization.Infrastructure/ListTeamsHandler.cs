using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="ListTeamsQuery"/>. RLS auto-filters
/// cross-tenant rows so the result set is implicitly scoped to the current
/// tenant (ADR-0090). Pagination applied via
/// <see cref="QueryablePagingExtensions.ToCursorPagedAsync{T}"/> (ADR-0095).
/// </summary>
public sealed class ListTeamsHandler
{
    // The separate IdExtractor accesses the primary key in-memory via the
    // domain property (x.Id.Value) for cursor encoding — EF.Property is not
    // invokable outside of an EF query context. TeamSortSpecs.IdSelector
    // provides the EF-translatable expression. See QueryablePagingExtensions
    // for the dual-expression overload that accommodates this split.
    private static readonly Func<Team, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<TeamResponse>> Handle(
        ListTeamsQuery q,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var spec = TeamSortSpecs.Resolve(q.SortBy);

        // Apply the displayName filter BEFORE pagination so a hidden row never
        // becomes a cursor boundary (same invariant as ListApplicationsHandler).
        IQueryable<Team> source = db.Teams;
        Dictionary<string, string>? filters = null;
        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(t => EF.Functions.ILike(t.DisplayName, pattern, "\\"));
            // The owning module owns the f-map keys/values; the shared codec treats
            // them as opaque. A change mid-pagination trips CursorFilterMismatchException.
            filters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["displayNameContains"] = name,
            };
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                TeamSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        var items = page.Items
            .Select(t => new TeamResponse(t.Id.Value, t.DisplayName, t.Description, t.CreatedAt))
            .ToList();

        return new CursorPage<TeamResponse>(items, page.NextCursor, page.PrevCursor);
    }

}
