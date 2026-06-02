namespace Kartova.Organization.Contracts;

/// <summary>
/// Public sort-field allow-list for <c>GET /api/v1/organizations/invitations</c>.
/// Surfaces in OpenAPI as <c>SortByInvitations</c>. ADR-0095.
/// </summary>
public enum InvitationSortField
{
    InvitedAt,
    ExpiresAt,
    Email,
}
