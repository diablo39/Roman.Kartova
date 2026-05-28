using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Wire shape for a single invitation row. Surfaced by
/// <c>POST /api/v1/organizations/invitations</c> (nested inside
/// <see cref="CreateInvitationResponse"/>) and
/// <c>GET /api/v1/organizations/invitations</c> (slice 9 spec §6.7).
/// <see cref="Status"/> is the string form of
/// <c>Kartova.Organization.Domain.InvitationStatus</c>:
/// <c>Pending</c> | <c>Accepted</c> | <c>Revoked</c> | <c>Expired</c>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record InvitationResponse(
    Guid Id,
    string Email,
    string Role,
    DateTimeOffset InvitedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    Guid InvitedByUserId,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt);
