using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="ListApplicationsQuery"/>. RLS auto-filters cross-tenant
/// rows so the result set is implicitly scoped to the current tenant (ADR-0090).
/// Pagination applied via <see cref="QueryablePagingExtensions.ToCursorPagedAsync{T}"/>
/// (ADR-0095).
/// <para>
/// Slice 9 / E1 (ADR-0098): each returned row is enriched with the
/// owner's display name via the <see cref="IUserDirectory"/> cross-module port.
/// The lookup is batched (<see cref="IUserDirectory.GetManyAsync"/>) over the
/// distinct owner ids on the current page so it costs at most one extra round
/// trip regardless of page size. The port returns null entries for ids that
/// have no matching <c>users</c> row (e.g., the user was deleted after the
/// application was registered) — those rows carry <c>Owner = null</c> in the
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

        // Apply ADR-0073 default-view filter before pagination so the keyset
        // bounds stay consistent: a row that's hidden by the filter must never
        // appear as a cursor boundary, otherwise the next page would silently
        // skip rows. The cursor JSON (CursorCodec `f`) is mismatch-checked inside
        // ToCursorPagedAsync.
        IQueryable<DomainApplication> source = db.Applications;
        if (!q.IncludeDecommissioned)
        {
            source = source.Where(a => a.Lifecycle != Lifecycle.Decommissioned);
        }

        // Slice 9 / E2 (spec §6.5): optional ownerUserId filter. Endpoint-level
        // IUserDirectory validation guarantees that, if non-null, the supplied
        // id resolves to a user in the current tenant — so by the time we get
        // here the predicate is safe to apply. RLS still scopes the row set, so
        // a leak-by-construction (e.g., owner row from another tenant) would
        // still produce an empty page rather than expose cross-tenant data.
        // Slice 9 / S5 carry-forward: ownerUserId is encoded into the generic
        // filter map below (CursorCodec `f`); ToCursorPagedAsync replays the map
        // on cursor decode and trips CursorFilterMismatchException if the request
        // changes the owner mid-pagination. Same mechanism as includeDecommissioned.
        if (q.OwnerUserId is { } ownerUserId)
        {
            source = source.Where(a => a.OwnerUserId == ownerUserId);
        }

        // Filter state the cursor is issued under (ADR-0095). The owning module
        // owns the keys/values; the shared codec treats them as opaque. Always-
        // applied dimensions (includeDecommissioned) are always present; optional
        // filters (ownerUserId) only when applied. A change mid-pagination trips
        // CursorFilterMismatchException inside ToCursorPagedAsync.
        var filters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["includeDecommissioned"] = q.IncludeDecommissioned ? "true" : "false",
        };
        if (q.OwnerUserId is { } owner)
        {
            filters["ownerUserId"] = owner.ToString("D");
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApplicationSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        // Batch-fetch owners for the entire page in a single round trip. HashSet
        // de-duplicates in one allocation so multiple apps owned by the same user
        // cost only one entry in the lookup payload.
        var ownerIds = new HashSet<Guid>(page.Items.Select(a => a.OwnerUserId));
        var owners = await directory.GetManyAsync(ownerIds, ct);

        var items = page.Items
            .Select(r =>
            {
                var resp = r.ToResponse();
                return owners.TryGetValue(r.OwnerUserId, out var owner)
                    ? resp with { Owner = owner }
                    : resp;
            })
            .ToList();
        return new CursorPage<ApplicationResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
