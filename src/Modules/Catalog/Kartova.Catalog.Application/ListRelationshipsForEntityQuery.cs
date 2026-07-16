using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

public sealed record ListRelationshipsForEntityQuery(
    EntityRef Entity, RelationshipDirection Direction,
    RelationshipSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit,
    bool ExcludeApiEdges = false);
