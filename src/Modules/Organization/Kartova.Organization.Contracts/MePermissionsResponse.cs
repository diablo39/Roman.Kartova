using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record MePermissionsResponse(string? Role, IReadOnlyCollection<string> Permissions);
