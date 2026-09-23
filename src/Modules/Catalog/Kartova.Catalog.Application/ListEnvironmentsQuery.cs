using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List environments visible to the current tenant (RLS-filtered). ADR-0095/0107.
/// <paramref name="Type"/> — multi-select tier filter. <paramref name="Region"/> — exact-match
/// column filter. <paramref name="DisplayNameContains"/> — case-insensitive substring (ILIKE).
/// Each null ⇒ no predicate.</summary>
public sealed record ListEnvironmentsQuery(
    EnvironmentSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    EnvironmentType[]? Type = null,
    string? Region = null,
    string? DisplayNameContains = null);
