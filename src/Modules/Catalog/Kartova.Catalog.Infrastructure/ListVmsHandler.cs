using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="ListVmsQuery"/>. Type is fixed to
/// <see cref="InfrastructureType.VirtualMachine"/>; attribute filters translate to Postgres
/// jsonb containment (<c>@&gt;</c>, GIN-indexed) via <c>EF.Functions.JsonContains</c> — object
/// containment for <c>powerState/os/region/hostname</c>, array containment for
/// <c>ipAddresses</c>. Each page row is rehydrated to <see cref="VmAttributesDto"/> in memory
/// after the DB round trip (ADR-0111 amendment).
/// <para>
/// <c>EF.Functions.JsonContains(object json, object contained)</c> takes <c>object</c> for both
/// parameters, so a plain <c>string</c> json literal is passed directly (no cast needed) — see
/// <see cref="Contains"/>/<see cref="ContainsArray"/>. EF's translation of this call against
/// real Postgres is verified by Task 12's integration tests, not here.
/// </para>
/// </summary>
public sealed class ListVmsHandler
{
    private static readonly Func<InfrastructureResource, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<VmListItemResponse>> Handle(
        ListVmsQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = InfrastructureSortSpecs.Resolve(q.SortBy);
        IQueryable<InfrastructureResource> source =
            db.Infrastructure.Where(x => x.Type == InfrastructureType.VirtualMachine);

        if (q.TeamId.Length > 0)
            source = source.Where(x => q.TeamId.Contains(x.TeamId));
        if (q.PowerState is { } ps)
            source = source.Where(x => EF.Functions.JsonContains(x.Attributes, Contains("powerState", ps)));
        if (q.Os is { } os)
            source = source.Where(x => EF.Functions.JsonContains(x.Attributes, Contains("os", os)));
        if (q.Region is { } r)
            source = source.Where(x => EF.Functions.JsonContains(x.Attributes, Contains("region", r)));
        if (q.Hostname is { } h)
            source = source.Where(x => EF.Functions.JsonContains(x.Attributes, Contains("hostname", h)));
        if (q.IpAddress is { } ip)
            source = source.Where(x => EF.Functions.JsonContains(x.Attributes, ContainsArray("ipAddresses", ip)));

        var filters = BuildFilterMap(q);   // unit-tested helper

        var page = await source.ToCursorPagedAsync(
            spec, q.SortOrder, q.Cursor, q.Limit,
            InfrastructureSortSpecs.IdSelector, IdExtractor, ct, expectedFilters: filters);

        var items = page.Items.Select(x =>
        {
            var attrs = VmAttributes.FromJson(x.Attributes).ToDto();
            return new VmListItemResponse(x.Id.Value, x.TenantId.Value, x.DisplayName, x.Description,
                x.TeamId, x.SystemId, x.CreatedByUserId, x.CreatedAt, attrs);
        }).ToList();
        return new CursorPage<VmListItemResponse>(items, page.NextCursor, page.PrevCursor);
    }

    /// <summary>Builds a single-key object-containment JSON literal, e.g. <c>{"powerState":"running"}</c>.</summary>
    internal static string Contains(string key, string value) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { [key] = value });

    /// <summary>Builds a single-key array-containment JSON literal, e.g. <c>{"ipAddresses":["10.0.0.1"]}</c>.</summary>
    internal static string ContainsArray(string key, string value) =>
        JsonSerializer.Serialize(new Dictionary<string, string[]> { [key] = [value] });

    /// <summary>
    /// Builds the cursor f-map dict. Only non-empty filter dimensions are encoded so the
    /// cursor stays canonical and a mid-pagination change trips CursorFilterMismatchException.
    /// Keys are the camelCase wire names for each dimension; <c>ipAddress</c> encodes under the
    /// jsonb key <c>ipAddresses</c> to match the attribute payload's array key.
    /// </summary>
    internal static Dictionary<string, string>? BuildFilterMap(ListVmsQuery q)
    {
        if (q.TeamId.Length == 0 && q.PowerState is null && q.Os is null
            && q.Region is null && q.Hostname is null && q.IpAddress is null)
        {
            return null;
        }

        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q.TeamId.Length > 0)
            filters["teamId"] = CursorFilterValues.Join(q.TeamId);
        if (q.PowerState is { } ps)
            filters["powerState"] = ps;
        if (q.Os is { } os)
            filters["os"] = os;
        if (q.Region is { } r)
            filters["region"] = r;
        if (q.Hostname is { } h)
            filters["hostname"] = h;
        if (q.IpAddress is { } ip)
            filters["ipAddresses"] = ip;
        return filters;
    }
}
