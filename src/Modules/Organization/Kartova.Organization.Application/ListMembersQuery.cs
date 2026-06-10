using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Organization.Application;

/// <summary>
/// Cursor-paginated members directory query (slice 10 spec §4).
/// RLS filters rows to the current tenant automatically — no explicit
/// tenant predicate is needed here (ADR-0090). ADR-0095.
/// </summary>
public sealed record ListMembersQuery(
    MemberSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    string? Role,
    string? Q);
