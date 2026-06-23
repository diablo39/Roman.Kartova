using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Identity;
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
/// <para>
/// Slice 9 / E1 (ADR-0098): each returned row is enriched with the
/// creator's display name via the <see cref="IUserDirectory"/> cross-module port.
/// The lookup is batched (<see cref="IUserDirectory.GetManyAsync"/>) over the
/// distinct created-by ids on the current page so it costs at most one extra round
/// trip regardless of page size. The port returns null entries for ids that
/// have no matching <c>users</c> row (e.g., the creator was offboarded after the
/// application was registered) — those rows carry <c>CreatedBy = null</c> in the
/// response, which the wire contract allows.
/// </para>
/// </summary>
public sealed class ListApplicationsHandler(IUserDirectory directory)
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

        IQueryable<DomainApplication> source = db.Applications;

        // Lifecycle filter (ADR-0107) replaces the old includeDecommissioned boolean.
        // None selected ⇒ ADR-0073 default view (hide Decommissioned); some selected ⇒
        // exactly those states. Applied before paging so a hidden row never becomes a
        // cursor boundary. Array.Contains → SQL `= ANY(@p)` via Npgsql.
        if (q.Lifecycle.Length > 0)
        {
            source = source.Where(a => q.Lifecycle.Contains(a.Lifecycle));
        }
        else
        {
            source = source.Where(a => a.Lifecycle != Lifecycle.Decommissioned);
        }

        // Slice 9 / E2 (spec §6.5), reframed slice 10 / ADR-0103: optional
        // createdByUserId filter. Endpoint-level IUserDirectory validation
        // guarantees that, if non-null, the supplied id resolves to a user in the
        // current tenant — so by the time we get here the predicate is safe to
        // apply. RLS still scopes the row set, so a leak-by-construction (e.g., a
        // created-by row from another tenant) would still produce an empty page
        // rather than expose cross-tenant data.
        if (q.CreatedByUserId is { } createdByUserId)
        {
            source = source.Where(a => a.CreatedByUserId == createdByUserId);
        }

        // displayName contains filter (ADR-0107). Applied before paging so a hidden row
        // never becomes a cursor boundary.
        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(a => EF.Functions.ILike(a.DisplayName, pattern, "\\"));
        }

        // Team filter (ADR-0107). Applied before paging. Array.Contains → SQL IN.
        if (q.TeamId.Length > 0)
        {
            source = source.Where(a => q.TeamId.Contains(a.TeamId));
        }

        // Filter state the cursor is issued under (ADR-0095). Every applied filter is
        // recorded; absent filters add no key — so the default (unfiltered) cursor map
        // is EMPTY (byte-identical to a filterless cursor). Multi-value filters serialize
        // as sorted comma-joined strings so identity is order-independent. A change
        // mid-pagination trips CursorFilterMismatchException inside ToCursorPagedAsync.
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q.Lifecycle.Length > 0)
        {
            filters["lifecycle"] = string.Join(",",
                q.Lifecycle.Select(l => l.ToString()).OrderBy(s => s, StringComparer.Ordinal));
        }
        if (q.TeamId.Length > 0)
        {
            filters["teamId"] = string.Join(",",
                q.TeamId.Select(t => t.ToString("D")).OrderBy(s => s, StringComparer.Ordinal));
        }
        if (q.CreatedByUserId is { } createdBy)
        {
            filters["createdByUserId"] = createdBy.ToString("D");
        }
        if (q.DisplayNameContains is { } displayName)
        {
            filters["displayNameContains"] = displayName;
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApplicationSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        // Batch-fetch creators for the entire page in a single round trip. HashSet
        // de-duplicates in one allocation so multiple apps created by the same user
        // cost only one entry in the lookup payload.
        var creatorIds = new HashSet<Guid>(page.Items.Select(a => a.CreatedByUserId));
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
        return new CursorPage<ApplicationResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
