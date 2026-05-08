using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// List applications visible to the current tenant (RLS-filtered). ADR-0095.
/// <para>
/// <paramref name="IncludeDecommissioned"/> opts out of ADR-0073's
/// "filtered out of default views" rule; default false. Slice 6 / spec §5.
/// </para>
/// </summary>
public sealed record ListApplicationsQuery(
    ApplicationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    bool IncludeDecommissioned);
