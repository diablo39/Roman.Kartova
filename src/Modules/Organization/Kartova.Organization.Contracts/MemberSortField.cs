namespace Kartova.Organization.Contracts;

/// <summary>
/// Public sort-field allowlist for <c>GET /api/v1/organizations/users</c> (members directory).
/// ADR-0095.
/// </summary>
public enum MemberSortField
{
    DisplayName,
    Role,
    CreatedAt,
}
