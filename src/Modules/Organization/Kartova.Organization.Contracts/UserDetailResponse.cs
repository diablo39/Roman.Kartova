using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UserDetailResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? GivenName,
    string? FamilyName,
    IReadOnlyList<UserTeamMembership> Teams,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt);
