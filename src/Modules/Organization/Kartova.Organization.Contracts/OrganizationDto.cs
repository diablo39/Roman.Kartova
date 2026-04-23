namespace Kartova.Organization.Contracts;

public sealed record OrganizationDto(Guid Id, Guid TenantId, string Name, DateTimeOffset CreatedAt);
