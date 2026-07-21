using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List Systems visible to the current tenant (RLS-filtered), cursor-paginated (ADR-0095).
/// <para><paramref name="TeamId"/> — ADR-0107 multi-select steward-team filter
/// (<c>Array.Contains(column) → = ANY(@p)</c>). Empty ⇒ no predicate. Encoded in the cursor
/// f-map (sorted <c>Guid.ToString("D")</c>) when non-empty.</para>
/// <para><paramref name="DisplayNameContains"/> — ADR-0107 substring filter (ILIKE + backslash
/// escape). null/blank ⇒ no predicate + no f-map key; trimmed when present.</para></summary>
public sealed record ListSystemsQuery(
    SystemSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Guid[] TeamId,
    string? DisplayNameContains = null);
