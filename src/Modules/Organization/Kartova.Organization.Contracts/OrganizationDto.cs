using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record OrganizationDto(Guid Id, Guid TenantId, string Name, DateTimeOffset CreatedAt);
