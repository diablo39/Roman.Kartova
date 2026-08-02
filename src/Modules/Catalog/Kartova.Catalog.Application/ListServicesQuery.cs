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
/// <para>
/// <paramref name="SystemId"/> — ADR-0107 multi-select System filter (A2). Non-empty ⇒
/// rows having a <c>PartOf</c> edge to one of the supplied Systems (<c>EXISTS</c> sub-query
/// over <c>catalog_relationships</c>; there is no <c>system_id</c> column). <c>null</c> or
/// empty ⇒ no predicate. Encoded into the cursor f-map (sorted comma-joined Guid "D"
/// strings) only when non-empty. Declared as a trailing optional so the record's existing
/// named-argument call sites keep compiling; a record cannot default an array to
/// <c>Array.Empty&lt;Guid&gt;()</c>, so <c>null</c> and empty both mean "absent" and the
/// handler normalizes them in one place.
/// </para>
/// </summary>
public sealed record ListServicesQuery(
    ServiceSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Guid[] TeamId,
    HealthStatus[] Health,
    string? DisplayNameContains = null,
    Guid[]? SystemId = null);
