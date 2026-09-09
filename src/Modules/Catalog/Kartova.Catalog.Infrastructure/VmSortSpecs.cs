using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Per-resource sort allowlist for <c>GET /catalog/infrastructure/vms</c> (ADR-0095 §5,
/// ADR-0115 slice 2a). Lifts the JSONB-attribute-sort deferral noted on
/// <see cref="InfrastructureSortField"/> — <see cref="Kartova.Catalog.Contracts.VmSortField"/>'s
/// JSONB members (powerState/os/vcpu/memoryGb/hostname/region) are each served by a dedicated
/// partial btree-expression index (<c>WHERE type = 0</c>) in the
/// <c>AddInfrastructureProviderAndSortIndexes</c> migration.
/// <para>
/// <b>Expression discipline (critical):</b> each JSONB selector's EF-translated SQL MUST be
/// byte-identical to its partial index expression, or Postgres falls back to a sequential scan
/// and cursor keyset paging becomes non-deterministic across pages. The selectors below were
/// verified (via a throwaway <c>.ToQueryString()</c> capture on a VM-filtered
/// <c>CatalogDbContext.Infrastructure</c> query ordered by each <c>KeySelector</c>) to translate
/// to <c>jsonb_extract_path_text(c.attributes, 'key')</c> for the text members and
/// <c>jsonb_extract_path_text(c.attributes, 'key')::int</c> for the int members (via
/// <see cref="Convert.ToInt32(string?)"/>) — the migration's index DDL is authored from that
/// exact captured fragment, and <c>InfrastructureVmSortTests</c>' raw <c>EXPLAIN</c> test proves
/// the match holds against real Postgres (index used, no <c>Seq Scan</c>).
/// </para>
/// </summary>
internal static class VmSortSpecs
{
    public static readonly Expression<Func<InfrastructureResource, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfInfrastructureConfiguration.IdFieldName);

    /// <summary>Re-exports the shadow-PK field name for correlated sub-queries (mirrors
    /// <see cref="InfrastructureSortSpecs.IdFieldName"/>).</summary>
    public static string IdFieldName => EfInfrastructureConfiguration.IdFieldName;

    public static readonly SortSpec<InfrastructureResource> DisplayName = new("displayName", x => x.DisplayName);
    public static readonly SortSpec<InfrastructureResource> CreatedAt = new("createdAt", x => x.CreatedAt);
    public static readonly SortSpec<InfrastructureResource> Provider = new("provider", x => x.Provider!);

    public static readonly SortSpec<InfrastructureResource> PowerState =
        new("powerState", x => JsonbFunctions.JsonbExtractPathText(x.Attributes, "powerState")!);
    public static readonly SortSpec<InfrastructureResource> Os =
        new("os", x => JsonbFunctions.JsonbExtractPathText(x.Attributes, "os")!);
    public static readonly SortSpec<InfrastructureResource> Hostname =
        new("hostname", x => JsonbFunctions.JsonbExtractPathText(x.Attributes, "hostname")!);
    public static readonly SortSpec<InfrastructureResource> Region =
        new("region", x => JsonbFunctions.JsonbExtractPathText(x.Attributes, "region")!);
    public static readonly SortSpec<InfrastructureResource> Vcpu =
        new("vcpu", x => Convert.ToInt32(JsonbFunctions.JsonbExtractPathText(x.Attributes, "vcpu")));
    public static readonly SortSpec<InfrastructureResource> MemoryGb =
        new("memoryGb", x => Convert.ToInt32(JsonbFunctions.JsonbExtractPathText(x.Attributes, "memoryGb")));

    public static readonly IReadOnlyList<string> AllowedFieldNames =
    [
        DisplayName.FieldName, CreatedAt.FieldName, Provider.FieldName,
        PowerState.FieldName, Os.FieldName, Vcpu.FieldName, MemoryGb.FieldName,
        Hostname.FieldName, Region.FieldName,
    ];

    public static SortSpec<InfrastructureResource> Resolve(VmSortField field) => field switch
    {
        VmSortField.DisplayName => DisplayName,
        VmSortField.CreatedAt => CreatedAt,
        VmSortField.Provider => Provider,
        VmSortField.PowerState => PowerState,
        VmSortField.Os => Os,
        VmSortField.Vcpu => Vcpu,
        VmSortField.MemoryGb => MemoryGb,
        VmSortField.Hostname => Hostname,
        VmSortField.Region => Region,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
