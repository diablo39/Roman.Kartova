using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record TeamResponse(Guid Id, string DisplayName, string? Description, DateTimeOffset CreatedAt);
