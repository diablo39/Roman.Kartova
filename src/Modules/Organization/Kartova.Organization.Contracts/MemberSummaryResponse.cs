using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Wire response for a single row in <c>GET /api/v1/organizations/users</c>
/// (members directory). <see cref="TeamCount"/> is the number of teams the user
/// belongs to within the current tenant. ADR-0095.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record MemberSummaryResponse(
    Guid Id,
    string DisplayName,
    string Email,
    string Role,
    int TeamCount,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt);
