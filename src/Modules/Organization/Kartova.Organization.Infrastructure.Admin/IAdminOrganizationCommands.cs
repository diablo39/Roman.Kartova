using Kartova.Organization.Contracts;

namespace Kartova.Organization.Infrastructure.Admin;

public interface IAdminOrganizationCommands
{
    Task<OrganizationDto> CreateAsync(string name, CancellationToken ct);
}
