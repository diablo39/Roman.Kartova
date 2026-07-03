using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List APIs visible to the current tenant (RLS-filtered), cursor-paginated
/// (ADR-0095). No attribute filters this slice — style/team filtering is deferred to the
/// API-UI slice (spec §11 FU-9).</summary>
public sealed record ListApisQuery(
    ApiSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit);
