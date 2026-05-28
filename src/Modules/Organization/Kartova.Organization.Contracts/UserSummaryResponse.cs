using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UserSummaryResponse(Guid Id, string DisplayName, string Email);
