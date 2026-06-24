using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
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
/// <para>
/// <paramref name="TeamId"/> — ADR-0107 multi-select team filter. Non-empty ⇒ rows
/// whose <c>TeamId</c> is in the supplied set (<c>Array.Contains(column) → SQL = ANY(@p)</c>
/// via Npgsql). Empty ⇒ no predicate (show all teams). Encoded into the cursor f-map
/// (sorted <c>Guid.ToString("D")</c>) only when non-empty.
/// </para>
/// <para>
/// <paramref name="Health"/> — ADR-0107 multi-select health filter. Non-empty ⇒ rows
/// whose <c>Health</c> is in the supplied set (<c>Array.Contains(column) → SQL = ANY(@p)</c>
/// via Npgsql). Empty ⇒ no predicate (show all health statuses). Encoded into the cursor
/// f-map (sorted enum names) only when non-empty.
/// </para>
/// </summary>
public sealed record ListServicesQuery(
    ServiceSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Guid[] TeamId,
    HealthStatus[] Health,
    string? DisplayNameContains = null);
