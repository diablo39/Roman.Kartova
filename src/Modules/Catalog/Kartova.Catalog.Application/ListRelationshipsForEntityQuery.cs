using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// <paramref name="Type"/> — optional single-valued relationship-type filter (E-03
/// slice A1 task 4d). Narrows the page to edges of exactly this <see cref="RelationshipType"/>
/// before pagination and is encoded into the cursor's filter map (ADR-0095) so a
/// mid-pagination change of <c>type</c> trips <see cref="Kartova.SharedKernel.Pagination.CursorFilterMismatchException"/>
/// rather than silently mixing pages. Null (the default) omits the filter entirely,
/// leaving pre-existing callers byte-identical.
/// </summary>
public sealed record ListRelationshipsForEntityQuery(
    EntityRef Entity, RelationshipDirection Direction,
    RelationshipSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit,
    bool ExcludeApiEdges = false,
    RelationshipType? Type = null);
