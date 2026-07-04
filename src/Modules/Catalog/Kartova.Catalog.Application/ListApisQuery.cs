using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List APIs visible to the current tenant (RLS-filtered), cursor-paginated (ADR-0095).
/// <para><paramref name="TeamId"/> — ADR-0107 multi-select team filter (<c>Array.Contains(column) → = ANY(@p)</c>).
/// Empty ⇒ no predicate. Encoded in the cursor f-map (sorted <c>Guid.ToString("D")</c>) when non-empty.</para>
/// <para><paramref name="Style"/> — ADR-0107 multi-select style filter (enum-in-set). Empty ⇒ no predicate.
/// Encoded in the f-map (sorted enum names) when non-empty.</para>
/// <para><paramref name="DisplayNameContains"/> — ADR-0107 substring filter (ILIKE + backslash escape).
/// null/blank ⇒ no predicate + no f-map key; trimmed when present.</para></summary>
public sealed record ListApisQuery(
    ApiSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Guid[] TeamId,
    ApiStyle[] Style,
    string? DisplayNameContains = null);
