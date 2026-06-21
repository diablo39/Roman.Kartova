using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Organization.Application;

/// <summary>
/// List teams visible to the current tenant (RLS-filtered). ADR-0095.
/// </summary>
public sealed record ListTeamsQuery(
    TeamSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    string? DisplayNameContains);
