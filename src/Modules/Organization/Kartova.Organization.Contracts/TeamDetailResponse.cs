using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record TeamDetailResponse(
    Guid Id,
    string DisplayName,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<TeamMemberResponse> Members,
    IReadOnlyCollection<TeamApplicationSummary> Applications);

[ExcludeFromCodeCoverage]
public sealed record TeamApplicationSummary(Guid Id, string DisplayName, string Lifecycle);
