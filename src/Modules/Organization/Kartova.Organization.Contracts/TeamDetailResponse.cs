using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record TeamDetailResponse(
    Guid Id,
    string DisplayName,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<TeamMemberResponse> Members,
    IReadOnlyCollection<Guid> ApplicationIds);
