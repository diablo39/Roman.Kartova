using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Organization.Application;

/// <summary>
/// List invitations visible to the current tenant (RLS-filtered). Optionally
/// filter by Status (Pending/Accepted/Revoked/Expired); null means "all".
/// Sort by InvitedAt, ExpiresAt, or Email. Cursor-paginated per ADR-0095.
/// </summary>
public sealed record ListInvitationsQuery(
    InvitationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    InvitationStatus? StatusFilter);
