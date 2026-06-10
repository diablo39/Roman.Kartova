using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Handler for <see cref="ListMembersQuery"/>. Applies optional <c>role</c> and
/// infix-text (<c>q</c>) filters before paginating, then batch-loads team counts
/// for the returned page. RLS auto-scopes all queries to the current tenant
/// (ADR-0090) — no explicit tenant predicate is added here.
/// <para>
/// Case-insensitive infix search uses <c>string.ToLower().Contains()</c> so the
/// predicate works on both the Npgsql provider (translates to <c>LOWER(col) LIKE</c>)
/// and any in-memory test provider — mirrors <see cref="UserQueries.SearchAsync"/>.
/// </para>
/// <para>
/// Applied filters are registered in <c>expectedFilters</c> per ADR-0095
/// cursor-filter-replay contract: a cursor issued under active <c>role</c>/<c>q</c>
/// filters is rejected if the next request changes those filters, preventing
/// silent row skips or duplicates.
/// </para>
/// </summary>
public sealed class ListMembersHandler
{
    public async Task<CursorPage<MemberSummaryResponse>> Handle(
        ListMembersQuery q, OrganizationDbContext db, CancellationToken ct)
    {
        var spec = MemberSortSpecs.Resolve(q.SortBy);

        IQueryable<User> query = db.Users;
        var expectedFilters = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(q.Role))
        {
            query = query.Where(u => u.RealmRole == q.Role);
            expectedFilters["role"] = q.Role;
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            var lowered = term.ToLowerInvariant();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(lowered) ||
                u.Email.ToLower().Contains(lowered));
            expectedFilters["q"] = term;
        }

        var page = await query.ToCursorPagedAsync(
            spec, q.SortOrder, q.Cursor, q.Limit,
            MemberSortSpecs.IdSelector, MemberSortSpecs.IdExtractor, ct,
            expectedFilters.Count > 0 ? expectedFilters : null);

        var ids = page.Items.Select(u => u.Id).ToList();

        var teamCounts = await db.TeamMembers
            .Where(m => ids.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countById = teamCounts.ToDictionary(x => x.UserId, x => x.Count);

        var items = page.Items
            .Select(u => new MemberSummaryResponse(
                u.Id,
                u.DisplayName,
                u.Email,
                u.RealmRole,
                countById.TryGetValue(u.Id, out var c) ? c : 0,
                u.LastSeenAt,
                u.CreatedAt))
            .ToList();

        return new CursorPage<MemberSummaryResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
