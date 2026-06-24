using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;
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

        // Apply filters BEFORE pagination so a hidden row never becomes a cursor boundary
        // (same invariant as ListApplicationsHandler / ListTeamsHandler).
        IQueryable<DomainService> source = db.Services;

        // teamId filter: Array.Contains(column) → SQL = ANY(@p) via Npgsql.
        if (q.TeamId.Length > 0)
            source = source.Where(s => q.TeamId.Contains(s.TeamId));

        // health filter: Array.Contains(column) → SQL = ANY(@p) via Npgsql.
        if (q.Health.Length > 0)
            source = source.Where(s => q.Health.Contains(s.Health));

        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(s => EF.Functions.ILike(s.DisplayName, pattern, "\\"));
        }

        // Build the f-map dict. Only non-empty filter dimensions are encoded so
        // the cursor stays canonical and a mid-pagination change trips
        // CursorFilterMismatchException. The owning module owns the f-map keys/values;
        // the shared codec treats them as opaque.
        Dictionary<string, string>? filters = null;
        if (q.TeamId.Length > 0 || q.Health.Length > 0 || q.DisplayNameContains is not null)
        {
            filters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (q.TeamId.Length > 0)
                // Sorted so the f-map value is canonical regardless of input order.
                filters["teamId"] = string.Join(",", q.TeamId.Select(g => g.ToString("D")).Order());
            if (q.Health.Length > 0)
                // Sorted enum names (same pattern as lifecycle in ListApplicationsHandler).
                filters["health"] = string.Join(",", q.Health.Select(h => h.ToString()).Order());
            if (q.DisplayNameContains is { } dn)
                filters["displayNameContains"] = dn;
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ServiceSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

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
