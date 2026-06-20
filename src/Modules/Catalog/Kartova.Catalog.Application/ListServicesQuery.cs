using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List services visible to the current tenant (RLS-filtered). ADR-0095.</summary>
public sealed record ListServicesQuery(
    ServiceSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit);
