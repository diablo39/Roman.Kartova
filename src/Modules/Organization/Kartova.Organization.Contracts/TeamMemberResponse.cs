using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record TeamMemberResponse(Guid UserId, string Role, DateTimeOffset AddedAt);
