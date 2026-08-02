using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;
using DomainService = Kartova.Catalog.Domain.Service;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="ListServicesQuery"/>. RLS scopes the result set;
/// keyset pagination via ToCursorPagedAsync (ADR-0095). Each page row is enriched with
/// the creator display name in one batched IUserDirectory round trip (mirrors
/// ListApplicationsHandler).
/// <para>
/// A2 (ADR-0107/ADR-0111): each row is also enriched with its current System membership via
/// <see cref="ISystemMembershipEnricher"/> — a thin port over
/// <see cref="CurrentMembershipQueries.SystemsForComponentsAsync"/>, batched per page the same
/// way. It is a port (not a direct static call) purely for unit-test isolation — see that
/// interface's doc for why.
/// </para>
/// </summary>
public sealed class ListServicesHandler(IUserDirectory directory, ISystemMembershipEnricher systemMembership)
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

        // System filter (A2) — EXISTS over the PartOf edge; see ListApplicationsHandler for the
        // full rationale on Target.Kind and Source.Kind. EntityKind.Service here, not Application.
        if (q.SystemId is { Length: > 0 } systemIds)
        {
            source = source.Where(s => db.Relationships.Any(r =>
                r.Type == RelationshipType.PartOf
                && r.Source.Kind == EntityKind.Service
                && r.Target.Kind == EntityKind.System
                && r.Source.Id == EF.Property<Guid>(s, ServiceSortSpecs.IdFieldName)
                && systemIds.Contains(r.Target.Id)));
        }

        // Build the f-map dict. Only non-empty filter dimensions are encoded so
        // the cursor stays canonical and a mid-pagination change trips
        // CursorFilterMismatchException. The owning module owns the f-map keys/values;
        // the shared codec treats them as opaque.
        Dictionary<string, string>? filters = null;
        if (q.TeamId.Length > 0 || q.Health.Length > 0 || q.DisplayNameContains is not null
            || q.SystemId is { Length: > 0 })
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
            // StringComparer.Ordinal, NOT the neighbouring `.Order()` above. `.Order()` resolves
            // to Comparer<string>.Default, which is culture-sensitive, while the decode-side
            // comparison (CursorFilterComparer) is strictly ordinal. Two API replicas under
            // different cultures would canonicalize the same filter set differently and emit
            // spurious cursor-filter-mismatch 400s. Benign for Guid "D" strings (ASCII) — but do
            // not copy `.Order()` forward for new keys.
            if (q.SystemId is { Length: > 0 } systemFilter)
                filters["systemId"] = string.Join(",",
                    systemFilter.Select(g => g.ToString("D")).OrderBy(s => s, StringComparer.Ordinal));
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ServiceSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);

        var creatorIds = new HashSet<Guid>(page.Items.Select(s => s.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        // One extra round trip for the whole page (NOT per row) — same discipline as the
        // creator enrichment above. Components with no PartOf edge are absent from the map and
        // keep SystemId/SystemDisplayName null, which the FE renders as "—".
        var systems = await systemMembership.SystemsForComponentsAsync(
            db, EntityKind.Service, [.. page.Items.Select(s => s.Id.Value)], ct);

        var items = page.Items
            .Select(r =>
            {
                var resp = r.ToResponse();
                if (creators.TryGetValue(r.CreatedByUserId, out var creator))
                    resp = resp with { CreatedBy = creator };
                if (systems.TryGetValue(r.Id.Value, out var system))
                    resp = resp with { SystemId = system.Id, SystemDisplayName = system.DisplayName };
                return resp;
            })
            .ToList();
        return new CursorPage<ServiceResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
