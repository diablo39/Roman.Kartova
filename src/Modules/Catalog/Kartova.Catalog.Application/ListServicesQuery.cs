using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// List services visible to the current tenant (RLS-filtered). ADR-0095.
/// <para>
/// <paramref name="DisplayNameContains"/> — ADR-0107 substring filter. Applied
/// before pagination so a hidden row never becomes a cursor boundary. ILIKE with
/// backslash escape; encoded into the cursor f-map so a mid-pagination change
/// trips CursorFilterMismatchException.
/// null = filter absent; trimmed non-whitespace when present; null/blank ⇒ no WHERE + no cursor f-map key.
/// </para>
/// </summary>
public sealed record ListServicesQuery(
    ServiceSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    string? DisplayNameContains);
